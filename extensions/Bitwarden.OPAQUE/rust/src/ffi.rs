use std::ffi::c_char;

use crate::{DefaultCipherSuite, Error, ffi_types::*};

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
pub unsafe extern "C" fn start_server_registration(
    request_bytes: Buffer,
    username: *const c_char,
) -> Response {
    let registration_request_bytes = unsafe { request_bytes.as_slice() };
    let username = match unsafe { handle_string_input(username, "username") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    let response = match super::server::start_server_registration::<DefaultCipherSuite>(
        registration_request_bytes,
        username,
    ) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok2(response.message, response.server_setup)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_server_registration(registration_upload_bytes: Buffer) -> Response {
    let registration_upload_bytes = unsafe { registration_upload_bytes.as_slice() };

    let response = match super::server::finish_server_registration::<DefaultCipherSuite>(
        registration_upload_bytes,
    ) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok1(response)
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

    let result = match super::client::start_client_registration::<DefaultCipherSuite>(password) {
        Ok(result) => result,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok2(result.message, result.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_registration(
    state_bytes: Buffer,
    registration_response_bytes: Buffer,
    password: *const c_char,
) -> Response {
    let registration_response_bytes = unsafe { registration_response_bytes.as_slice() };
    let state_bytes = unsafe { state_bytes.as_slice() };

    let password = match unsafe { handle_string_input(password, "password") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    let response = match super::client::finish_client_registration::<DefaultCipherSuite>(
        state_bytes,
        registration_response_bytes,
        password,
    ) {
        Ok(response) => response,
        Err(e) => {
            return Response::error(e);
        }
    };

    Response::ok3(response.message, response.export_key, response.server_s_pk)
}
