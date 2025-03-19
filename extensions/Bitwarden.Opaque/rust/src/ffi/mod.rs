use std::{ffi::c_char, str::FromStr};

use crate::{
    Error,
    opaque::{CipherConfiguration, OpaqueImpl},
    try_ffi,
};

mod types;

use types::*;

unsafe fn parse_str<'a>(input: *const c_char, name: &'static str) -> Result<&'a str, Error> {
    unsafe { std::ffi::CStr::from_ptr(input).to_str() }
        .map_err(|_| Error::InvalidInput(name.into()))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn register_seeded_fake_config(seed: Buffer) -> Response {
    let seed = try_ffi!(unsafe { seed.as_slice() });
    let seed = try_ffi!(
        <[u8; 32]>::try_from(seed).map_err(|_| Error::InvalidInput("Seed must be 32 bytes".into()))
    );

    let (server_setup, server_registration) =
        try_ffi!(crate::opaque::register_seeded_fake_config(seed));

    Response::ok2(server_setup, server_registration)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_registration(
    config: *const c_char,
    password: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let password: &str = try_ffi!(unsafe { parse_str(password, "password") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let result = try_ffi!(config.start_client_registration(password));

    Response::ok2(result.registration_request, result.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_registration(
    config: *const c_char,
    server_setup: Buffer,
    registration_request: Buffer,
    username: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let server_setup = unsafe { server_setup.as_slice() }.ok();
    let registration_request = try_ffi!(unsafe { registration_request.as_slice() });
    let username = try_ffi!(unsafe { parse_str(username, "username") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response =
        try_ffi!(config.start_server_registration(server_setup, registration_request, username));

    Response::ok2(response.registration_response, response.server_setup)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_registration(
    config: *const c_char,
    state: Buffer,
    registration_response: Buffer,
    password: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let registration_response = try_ffi!(unsafe { registration_response.as_slice() });
    let state = try_ffi!(unsafe { state.as_slice() });
    let password = try_ffi!(unsafe { parse_str(password, "password") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response =
        try_ffi!(config.finish_client_registration(state, registration_response, password));

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
pub unsafe extern "C" fn finish_server_registration(
    config: *const c_char,
    registration_upload: Buffer,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let registration_upload = try_ffi!(unsafe { registration_upload.as_slice() });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response = try_ffi!(config.finish_server_registration(registration_upload));
    Response::ok1(response.server_registration)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_login(
    config: *const c_char,
    password: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let password = try_ffi!(unsafe { parse_str(password, "password") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response = try_ffi!(config.start_client_login(password));
    Response::ok2(response.credential_request, response.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_login(
    config: *const c_char,
    server_setup: Buffer,
    server_registration: Buffer,
    credential_request: Buffer,
    username: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let server_setup = try_ffi!(unsafe { server_setup.as_slice() });
    let server_registration = try_ffi!(unsafe { server_registration.as_slice() });
    let credential_request = try_ffi!(unsafe { credential_request.as_slice() });
    let username = try_ffi!(unsafe { parse_str(username, "username") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response = try_ffi!(config.start_server_login(
        server_setup,
        server_registration,
        credential_request,
        username,
    ));
    Response::ok2(response.credential_response, response.state)
}

///
///
/// # Safety
/// ABC
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_login(
    config: *const c_char,
    state: Buffer,
    credential_response: Buffer,
    password: *const c_char,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let state = try_ffi!(unsafe { state.as_slice() });
    let credential_response = try_ffi!(unsafe { credential_response.as_slice() });
    let password = try_ffi!(unsafe { parse_str(password, "password") });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response = try_ffi!(config.finish_client_login(state, credential_response, password));
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
    config: *const c_char,
    state: Buffer,
    credential_finalization: Buffer,
) -> Response {
    let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
    let state = try_ffi!(unsafe { state.as_slice() });
    let credential_finalization = try_ffi!(unsafe { credential_finalization.as_slice() });

    let mut config = try_ffi!(CipherConfiguration::from_str(config));

    let response = try_ffi!(config.finish_server_login(state, credential_finalization));

    Response::ok1(response.session_key)
}
