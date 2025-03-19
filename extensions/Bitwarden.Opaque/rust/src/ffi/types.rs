use crate::Error;

/// Free the buffer memory.
///
/// # Safety
/// The parameter should contain a Buffer pointing to valid initialized memory, or null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn free_buffer(buf: Buffer) {
    unsafe { buf.free() };
}

#[repr(C)]
pub struct Buffer {
    data: *mut u8,
    len: usize,
}

///  A struct to represent a buffer of data.
///  Important: The structure of this type must match the structure
///   of the Buffer type in the C# BitwardenLibrary, both in field type and order.
impl Buffer {
    pub fn null() -> Self {
        Buffer {
            data: std::ptr::null_mut(),
            len: 0,
        }
    }

    pub fn from_vec(mut vec: Vec<u8>) -> Self {
        // Important: Ensure that capacity and length are the same.
        vec.shrink_to_fit();

        let len = vec.len();
        let data = vec.as_mut_ptr();
        std::mem::forget(vec);
        Buffer { data, len }
    }

    pub unsafe fn as_slice(&self) -> Result<&[u8], Error> {
        if self.data.is_null() {
            return Err(Error::InvalidInput("Buffer data is null".into()));
        }
        Ok(unsafe { std::slice::from_raw_parts(self.data, self.len) })
    }

    pub unsafe fn free(self) {
        if !self.data.is_null() {
            let _ = unsafe { Vec::from_raw_parts(self.data, self.len, self.len) };
        }
    }
}

#[macro_export]
macro_rules! try_ffi {
    ($e:expr) => {
        match $e {
            Ok(v) => v,
            Err(e) => return Response::error(e),
        }
    };
}

/// A struct to represent a response from the rust library.
/// Important: The structure of this type must match the structure of the
/// Response type in the C# BitwardenLibrary, both in field type and order.
#[repr(C)]
pub struct Response {
    pub error: usize,
    pub error_message: Buffer,

    // TODO: This is a way of returning multiple values without having different return FFI types.
    // Ideally we'd have a separate type per return type? Or maybe a better way to represent this.
    pub data1: Buffer,
    pub data2: Buffer,
    pub data3: Buffer,
    pub data4: Buffer,
}

impl Response {
    pub fn ok1(data1: Vec<u8>) -> Self {
        Response {
            error: 0,
            error_message: Buffer::null(),

            data1: Buffer::from_vec(data1),
            data2: Buffer::null(),
            data3: Buffer::null(),
            data4: Buffer::null(),
        }
    }

    pub fn ok2(data1: Vec<u8>, data2: Vec<u8>) -> Self {
        Response {
            error: 0,
            error_message: Buffer::null(),

            data1: Buffer::from_vec(data1),
            data2: Buffer::from_vec(data2),
            data3: Buffer::null(),
            data4: Buffer::null(),
        }
    }

    pub fn ok3(data1: Vec<u8>, data2: Vec<u8>, data3: Vec<u8>) -> Self {
        Response {
            error: 0,
            error_message: Buffer::null(),

            data1: Buffer::from_vec(data1),
            data2: Buffer::from_vec(data2),
            data3: Buffer::from_vec(data3),
            data4: Buffer::null(),
        }
    }

    pub fn ok4(data1: Vec<u8>, data2: Vec<u8>, data3: Vec<u8>, data4: Vec<u8>) -> Self {
        Response {
            error: 0,
            error_message: Buffer::null(),

            data1: Buffer::from_vec(data1),
            data2: Buffer::from_vec(data2),
            data3: Buffer::from_vec(data3),
            data4: Buffer::from_vec(data4),
        }
    }

    pub fn error(error: Error) -> Self {
        // Important: The error codes need to be kept in sync with the BitwardenException in C#.
        let (error, message) = match error {
            Error::InvalidInput(name) => (1, name),
            Error::InvalidConfig(error) => (2, error),
            Error::Protocol(e) => (3, format!("{:?}", e)),
            Error::InternalError(error) => (4, error),
        };

        Response {
            error,
            error_message: Buffer::from_vec(message.into_bytes()),

            data1: Buffer::null(),
            data2: Buffer::null(),
            data3: Buffer::null(),
            data4: Buffer::null(),
        }
    }
}
