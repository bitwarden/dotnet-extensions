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
    registration_request: Buffer,
    username: *const c_char,
) -> Response {
    let registration_request = unsafe { registration_request.as_slice() };
    let username = match unsafe { handle_string_input(username, "username") } {
        Ok(s) => s,
        Err(e) => {
            return e;
        }
    };

    let response = match super::server::start_server_registration::<DefaultCipherSuite>(
        registration_request,
        username,
    ) {
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
pub unsafe extern "C" fn finish_server_registration(registration_upload: Buffer) -> Response {
    let registration_upload = unsafe { registration_upload.as_slice() };

    let response = match super::server::finish_server_registration::<DefaultCipherSuite>(
        registration_upload,
    ) {
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

    Response::ok2(result.registration_request, result.state)
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

    let response = match super::client::finish_client_registration::<DefaultCipherSuite>(
        state,
        registration_response,
        password,
    ) {
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
