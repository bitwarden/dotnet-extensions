using System.Runtime.InteropServices;
using System.Text;

namespace Bitwarden.OPAQUE;

internal static partial class BitwardenLibrary
{

    /// <summary>
    ///  A struct to represent a buffer of data.
    ///  Important: The structure of this type must match the structure 
    ///   of the Buffer type in the rust crate, both in field type and order.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Buffer
    {
        public IntPtr data;
        public nint size;
    }

    /// <summary>
    ///  A struct to represent a response from the rust library.
    ///  Important: The structure of this type must match the structure 
    ///   of the Response type in the rust crate, both in field type and order.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Response
    {
        public nint error;
        public Buffer error_message;

        public Buffer data1;
        public Buffer data2;
        public Buffer data3;
    }

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void free_buffer(Buffer buf);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_server_registration(Buffer request_bytes, string username);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_server_registration(Buffer registration_upload_bytes);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_client_registration(string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_client_registration(Buffer state_bytes, Buffer registration_response_bytes, string password);


    private static Buffer BuildBuffer(byte[] data, out GCHandle handle)
    {
        handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        return new Buffer
        {
            data = handle.AddrOfPinnedObject(),
            size = data.Length
        };
    }

    private static byte[]? CopyAndFreeBuffer(Buffer buffer)
    {
        if (buffer.data == IntPtr.Zero) return null;
        if (buffer.size == 0) return [];

        var data = new byte[buffer.size];
        Marshal.Copy(buffer.data, data, 0, (int)buffer.size);
        free_buffer(buffer);
        return data;
    }

    private static List<byte[]> HandleResponse(Response response, int expectedValues)
    {
        // If we receive an error, parse the message and throw an exception
        if (response.error != 0)
        {
            var message = CopyAndFreeBuffer(response.error_message);
            string messageStr;
            try { messageStr = Encoding.UTF8.GetString(message!); } catch { messageStr = "<Can't decode>"; }
            throw new BitwardenException((int)response.error, messageStr);
        }

        // If we don't receive an error, parse all the return types
        var buffers = new Buffer[] { response.data1, response.data2, response.data3 };
        var arrays = new List<byte[]> { };

        foreach (var buffer in buffers)
        {
            var data = CopyAndFreeBuffer(buffer);
            if (data == null) break;
            arrays.Add(data);
        }
        if (arrays.Count != expectedValues)
        {
            throw new BitwardenException(100, $"Invalid number of return values. Expected {expectedValues}, got {arrays.Count}");
        }

        return arrays;
    }

    internal static (byte[], byte[]) StartServerRegistration(byte[] requestBytes, string username)
    {
        var requestBuffer = BuildBuffer(requestBytes, out var handle);
        try
        {
            var response = start_server_registration(requestBuffer, username);
            var ret = HandleResponse(response, 2);
            return (ret[0], ret[1]);
        }
        finally
        {
            handle.Free();
        }

    }

    internal static byte[] FinishServerRegistration(byte[] registrationUploadBytes)
    {
        var registrationUploadBuffer = BuildBuffer(registrationUploadBytes, out var handle);
        try
        {
            var response = finish_server_registration(registrationUploadBuffer);
            return HandleResponse(response, 1)[0];
        }
        finally
        {
            handle.Free();
        }

    }

    internal static (byte[], byte[]) StartClientRegistration(string password)
    {
        var response = start_client_registration(password);
        var ret = HandleResponse(response, 2);
        return (ret[0], ret[1]);
    }

    internal static (byte[], byte[], byte[]) FinishClientRegistration(byte[] stateBytes, byte[] registrationResponseBytes, string password)
    {
        var stateBuffer = BuildBuffer(stateBytes, out var stateHandle);
        var registrationResponseBuffer = BuildBuffer(registrationResponseBytes, out var registrationResponseHandle);

        try
        {
            var response = finish_client_registration(stateBuffer, registrationResponseBuffer, password);
            var ret = HandleResponse(response, 3);
            return (ret[0], ret[1], ret[2]);
        }
        finally
        {
            stateHandle.Free();
            registrationResponseHandle.Free();
        }
    }
}
