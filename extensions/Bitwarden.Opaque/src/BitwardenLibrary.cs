using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Opaque;

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
        public Buffer data4;
    }

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void free_buffer(Buffer buf);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response register_seeded_fake_config(Buffer seed);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_client_registration(string config, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_server_registration(string config, Buffer server_setup, Buffer registration_request, string username);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_client_registration(string config, Buffer state, Buffer registration_response, string password);


    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_server_registration(string config, Buffer registration_upload);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_client_login(string config, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response start_server_login(string config, Buffer server_setup, Buffer server_registration, Buffer credential_request, string username);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_client_login(string config, Buffer state, Buffer credential_response, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Response finish_server_login(string config, Buffer state, Buffer credential_finalization);

    private static Buffer BuildBuffer(byte[]? data, out GCHandle handle)
    {
        handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        return new Buffer
        {
            data = handle.AddrOfPinnedObject(),
            size = data?.Length ?? 0
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
        var buffers = new Buffer[] { response.data1, response.data2, response.data3, response.data4 };
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

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, // Converts enums to strings
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static (byte[], byte[]) RegisterSeededFakeConfig(byte[] seed)
    {
        var response = register_seeded_fake_config(BuildBuffer(seed, out var handle));
        try {
            var responseValues = HandleResponse(response, 2);
            return (responseValues[0], responseValues[1]); 
        } finally { handle.Free(); }
    }

    internal static (byte[], byte[]) StartClientRegistration(CipherConfiguration config, string password)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var response = start_client_registration(configStr, password);
        var ret = HandleResponse(response, 2);
        return (ret[0], ret[1]);
    }

    internal static (byte[], byte[]) StartServerRegistration(CipherConfiguration config, byte[]? serverSetup, byte[] registrationRequest, string username)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var serverSetupBuf = BuildBuffer(serverSetup, out var serverSetupHandle);
        var registrationRequestBuf = BuildBuffer(registrationRequest, out var registrationRequestHandle);
        try
        {
            var response = start_server_registration(configStr, serverSetupBuf, registrationRequestBuf, username);
            var ret = HandleResponse(response, 2);
            return (ret[0], ret[1]);
        }
        finally
        {
            serverSetupHandle.Free();
            registrationRequestHandle.Free();
        }
    }

    internal static (byte[], byte[], byte[]) FinishClientRegistration(CipherConfiguration config, byte[] state, byte[] registrationResponse, string password)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var stateBuf = BuildBuffer(state, out var stateHandle);
        var registrationResponseBuf = BuildBuffer(registrationResponse, out var registrationResponseHandle);

        try
        {
            var response = finish_client_registration(configStr, stateBuf, registrationResponseBuf, password);
            var ret = HandleResponse(response, 3);
            return (ret[0], ret[1], ret[2]);
        }
        finally
        {
            stateHandle.Free();
            registrationResponseHandle.Free();
        }
    }

    internal static byte[] FinishServerRegistration(CipherConfiguration config, byte[] registrationUpload)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var registrationUploadBuf = BuildBuffer(registrationUpload, out var handle);
        try
        {
            var response = finish_server_registration(configStr, registrationUploadBuf);
            return HandleResponse(response, 1)[0];
        }
        finally
        {
            handle.Free();
        }
    }

    internal static (byte[], byte[]) StartClientLogin(CipherConfiguration config, string password)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var response = start_client_login(configStr, password);
        var ret = HandleResponse(response, 2);
        return (ret[0], ret[1]);
    }

    internal static (byte[], byte[]) StartServerLogin(CipherConfiguration config, byte[] serverSetup, byte[] serverRegistration, byte[] credentialRequest, string username)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var serverSetupBuf = BuildBuffer(serverSetup, out var serverSetupHandle);
        var serverRegistrationBuf = BuildBuffer(serverRegistration, out var serverRegistrationHandle);
        var credentialRequestBuf = BuildBuffer(credentialRequest, out var credentialRequestHandle);
        try
        {
            var response = start_server_login(configStr, serverSetupBuf, serverRegistrationBuf, credentialRequestBuf, username);
            var ret = HandleResponse(response, 2);
            return (ret[0], ret[1]);
        }
        finally
        {
            serverSetupHandle.Free();
            serverRegistrationHandle.Free();
            credentialRequestHandle.Free();
        }

    }

    internal static (byte[], byte[], byte[], byte[]) FinishClientLogin(CipherConfiguration config, byte[] state, byte[] credentialResponse, string password)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var stateBuf = BuildBuffer(state, out var stateHandle);
        var credentialResponseBuf = BuildBuffer(credentialResponse, out var credentialResponseHandle);

        try
        {
            var response = finish_client_login(configStr, stateBuf, credentialResponseBuf, password);
            var ret = HandleResponse(response, 4);
            return (ret[0], ret[1], ret[2], ret[3]);
        }
        finally
        {
            stateHandle.Free();
            credentialResponseHandle.Free();
        }
    }

    internal static byte[] FinishServerLogin(CipherConfiguration config, byte[] state, byte[] credentialFinalization)
    {
        var configStr = JsonSerializer.Serialize(config, _serializerOptions);
        var stateBuf = BuildBuffer(state, out var stateHandle);
        var credentialFinalizationBuf = BuildBuffer(credentialFinalization, out var credentialFinalizationHandle);
        try
        {
            var response = finish_server_login(configStr, stateBuf, credentialFinalizationBuf);
            return HandleResponse(response, 1)[0];
        }
        finally
        {
            stateHandle.Free();
            credentialFinalizationHandle.Free();
        }

    }

}
