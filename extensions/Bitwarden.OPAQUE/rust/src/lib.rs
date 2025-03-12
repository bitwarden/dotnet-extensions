#![forbid(unsafe_op_in_unsafe_fn)]

use zeroizing_alloc::ZeroAlloc;

#[global_allocator]
static ALLOC: ZeroAlloc<std::alloc::System> = ZeroAlloc(std::alloc::System);

mod ffi;
mod opaque;

#[derive(Debug)]
pub enum Error {
    InvalidInput(&'static str),
    Protocol(opaque_ke::errors::ProtocolError),
}

impl From<opaque_ke::errors::ProtocolError> for Error {
    fn from(error: opaque_ke::errors::ProtocolError) -> Self {
        Self::Protocol(error)
    }
}

#[cfg(test)]
mod tests {
    use crate::opaque::*;

    #[test]
    fn test() {
        let password = "password";
        let username = "username";

        let config = CipherConfiguration::default();

        let registration_request = config.start_client_registration(password).unwrap();

        let server_start_result = config
            .start_server_registration(None, &registration_request.registration_request, username)
            .unwrap();

        let client_finish_result = config
            .finish_client_registration(
                &registration_request.state,
                &server_start_result.registration_response,
                password,
            )
            .unwrap();

        let server_finish_result = config
            .finish_server_registration(&client_finish_result.registration_upload)
            .unwrap();

        let _ = server_finish_result;
    }
}
