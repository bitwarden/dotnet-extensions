#![forbid(unsafe_op_in_unsafe_fn)]

use zeroizing_alloc::ZeroAlloc;

#[global_allocator]
static ALLOC: ZeroAlloc<std::alloc::System> = ZeroAlloc(std::alloc::System);

mod ffi;
mod opaque;

#[derive(Debug)]
pub enum Error {
    InvalidInput(String),
    InvalidConfig(String),
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

        // Registration

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

        // Login

        let login_request = config.start_client_login(password).unwrap();

        let server_login_result = config
            .start_server_login(
                &server_start_result.server_setup,
                &server_finish_result.server_registration,
                &login_request.credential_request,
                username,
            )
            .unwrap();

        let client_login_result = config
            .finish_client_login(
                &login_request.state,
                &server_login_result.credential_response,
                password,
            )
            .unwrap();

        let server_login_finish_result = config
            .finish_server_login(
                &server_login_result.state,
                &client_login_result.credential_finalization,
            )
            .unwrap();

        let _ = server_login_finish_result.session_key;
    }
}
