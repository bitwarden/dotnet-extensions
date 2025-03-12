use std::ffi::c_char;

use crate::{
    Error,
    opaque::{CipherConfiguration, OpaqueImpl},
};

mod types;

use types::*;

unsafe fn handle_string_input<'a>(
    input: *const c_char,
    name: &'static str,
) -> Result<&'a str, Response> {
    let input = unsafe { std::ffi::CStr::from_ptr(input).to_str() };
    match input {
        Ok(input) => Ok(input),
        Err(_) => Err(Response::error(Error::InvalidInput(name))),
    }
}
///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_registration(password: *const c_char) -> Response {
    let password = match unsafe { handle_string_input(password, "password") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let result = match config.start_client_registration(password) {
        Ok(result) => result,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok2(result.registration_request, result.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_registration(
    server_setup: Buffer,
    registration_request: Buffer,
    username: *const c_char,
) -> Response {
    let server_setup = unsafe { server_setup.as_slice2() }.ok();
    let registration_request = unsafe { registration_request.as_slice() };
    let username = match unsafe { handle_string_input(username, "username") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response =
        match config.start_server_registration(server_setup, registration_request, username) {
            Ok(response) => response,
            Err(e) => {
                return Response::error(e);
            }
        };

    Response::ok2(response.registration_response, response.server_setup)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_registration(
    state: Buffer,
    registration_response: Buffer,
    password: *const c_char,
) -> Response {
    let registration_response = unsafe { registration_response.as_slice() };
    let state = unsafe { state.as_slice() };

    let password = match unsafe { handle_string_input(password, "password") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.finish_client_registration(state, registration_response, password) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok3(
        response.registration_upload,
        response.export_key,
        response.server_s_pk,
    )
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_server_registration(registration_upload: Buffer) -> Response {
    let registration_upload = unsafe { registration_upload.as_slice() };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.finish_server_registration(registration_upload) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok1(response.server_registration)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_login(password: *const c_char) -> Response {
    let password = match unsafe { handle_string_input(password, "password") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.start_client_login(password) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok2(response.credential_request, response.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_login(
    server_setup: Buffer,
    server_registration: Buffer,
    credential_request: Buffer,
    username: *const c_char,
) -> Response {
    let server_setup = unsafe { server_setup.as_slice() };
    let server_registration = unsafe { server_registration.as_slice() };
    let credential_request = unsafe { credential_request.as_slice() };

    let username = match unsafe { handle_string_input(username, "username") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.start_server_login(
        server_setup,
        server_registration,
        credential_request,
        username,
    ) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok2(response.credential_response, response.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_login(
    state: Buffer,
    credential_response: Buffer,
    password: *const c_char,
) -> Response {
    let state = unsafe { state.as_slice() };
    let credential_response = unsafe { credential_response.as_slice() };
    let password = match unsafe { handle_string_input(password, "password") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.finish_client_login(state, credential_response, password) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok4(
        response.credential_finalization,
        response.session_key,
        response.export_key,
        response.server_s_pk,
    )
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_server_login(
    state: Buffer,
    credential_finalization: Buffer,
) -> Response {
    let state = unsafe { state.as_slice() };
    let credential_finalization = unsafe { credential_finalization.as_slice() };

    // TODO: Allow configuring the ciphers
    let config = CipherConfiguration::default();

    let response = match config.finish_server_login(state, credential_finalization) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok1(response.session_key)
}
