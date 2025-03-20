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
    internal struct Buffer
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
    internal struct Response
    {
        public nint error;
        public Buffer error_message;

        public Buffer data1;
        public Buffer data2;
        public Buffer data3;
        public Buffer data4;

        // Utility function to get all buffers as a list
        public readonly List<Buffer> GetAllBuffers()
        {
            return [data1, data2, data3, data4];
        }
    }

    // These are all the functions defined in the rust library.
    // Important: The function signatures must always match the signatures in the rust library.

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void free_buffer(Buffer buf);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response register_seeded_fake_config(Buffer seed);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response start_client_registration(string config, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response start_server_registration(string config, Buffer server_setup, Buffer registration_request, string username);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response finish_client_registration(string config, Buffer state, Buffer registration_response, string password);


    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response finish_server_registration(string config, Buffer registration_upload);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response start_client_login(string config, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response start_server_login(string config, Buffer server_setup, Buffer server_registration, Buffer credential_request, string username);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response finish_client_login(string config, Buffer state, Buffer credential_response, string password);

    [LibraryImport("opaque_ke_binding", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial Response finish_server_login(string config, Buffer state, Buffer credential_finalization);

    // This is an internal class to improve the FFI handling. It should not be created directly,
    // and should be used only from inside the ExecuteFFIFunction callback.
    internal class FFIHandler
    {
        private FFIHandler() { }

        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, // Converts enums to strings
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private readonly List<GCHandle> _handles = [];

        private void FreeHandles()
        {
            foreach (var handle in _handles) handle.Free();
            _handles.Clear();
        }
        /// Create an FFI buffer from the provided byte array.
        /// The buffer will be freed when the FFIHandler calls FreeHandles.
        /// Important: You should never use free_buffer on the buffer returned by this function,
        /// as it is only to be used with Rust allocated buffers. Doing otherwise is undefined behavior.
        public Buffer Buf(byte[]? data)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            _handles.Add(handle);
            return new Buffer
            {
                data = handle.AddrOfPinnedObject(),
                size = data?.Length ?? 0
            };
        }
        /// Serialize the provided configuration to a FFI string.
        public string Cfg(CipherConfiguration config)
        {
            return JsonSerializer.Serialize(config, _serializerOptions);
        }


        internal static List<byte[]> ExecuteFFIFunction(Func<FFIHandler, Response> function, int expectedValues)
        {

            static byte[]? CopyAndFreeBuffer(Buffer buffer)
            {
                if (buffer.data == IntPtr.Zero) return null;
                if (buffer.size == 0) return [];

                var data = new byte[buffer.size];
                Marshal.Copy(buffer.data, data, 0, (int)buffer.size);
                free_buffer(buffer);
                return data;
            }


            var ffi = new FFIHandler();
            try
            {
                // Execute the function and get the response
                var response = function(ffi);

                // If we receive an error, parse the message and throw an exception
                if (response.error != 0)
                {
                    var message = CopyAndFreeBuffer(response.error_message);
                    string messageStr;
                    try { messageStr = Encoding.UTF8.GetString(message!); } catch { messageStr = "<Can't decode>"; }
                    throw new BitwardenException((int)response.error, messageStr);
                }

                // If we don't receive an error, parse all the return types
                var arrays = new List<byte[]> { };
                foreach (var buffer in response.GetAllBuffers())
                {
                    var data = CopyAndFreeBuffer(buffer);
                    if (data == null) break;
                    arrays.Add(data);
                }

                // If we receive a different number of return values than expected, something must have gone wrong, throw an exception
                if (arrays.Count != expectedValues)
                {
                    throw new BitwardenException(100, $"Invalid number of return values. Expected {expectedValues}, got {arrays.Count}");
                }

                return arrays;
            }
            finally
            {
                ffi.FreeHandles();
            }
        }
    }

    // Just a wrapper to simplify calling
    internal static List<byte[]> ExecuteFFIFunction(Func<FFIHandler, Response> function, int expectedValues) => FFIHandler.ExecuteFFIFunction(function, expectedValues);
}
