use std::{ffi::c_char, panic::UnwindSafe, str::FromStr};

use crate::{
    Error,
    opaque::{CipherConfiguration, OpaqueImpl},
    try_ffi,
};

mod types;

use types::*;

/// # Safety
/// All the limitations of [std::ffi::CStr::from_ptr] apply, mainly:
/// - The pointer must be valid and point to a null-terminated byte string.
/// - The  memory must be valid for the duration of the call and not modified by other threads.
unsafe fn parse_str<'a>(input: *const c_char, name: &'static str) -> Result<&'a str, Error> {
    if input.is_null() {
        return Err(Error::InvalidInput("Input string is null".into()));
    }
    unsafe { std::ffi::CStr::from_ptr(input) }
        .to_str()
        .map_err(|_| Error::InvalidInput(name.into()))
}

fn catch(f: impl FnOnce() -> Response + UnwindSafe) -> Response {
    match std::panic::catch_unwind(f) {
        Ok(r) => r,
        Err(e) => Response::error(Error::InternalError(format!("Panic: {e:?}"))),
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn register_seeded_fake_config(seed: Buffer) -> Response {
    catch(|| {
        let seed = try_ffi!(unsafe { seed.as_slice() });
        let seed = try_ffi!(
            <[u8; 32]>::try_from(seed)
                .map_err(|_| Error::InvalidInput("Seed must be 32 bytes".into()))
        );

        let (server_setup, server_registration) =
            try_ffi!(crate::opaque::register_seeded_fake_config(seed));

        Response::ok2(server_setup, server_registration)
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_registration(
    config: *const c_char,
    password: *const c_char,
) -> Response {
    catch(|| {
        let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
        let password: &str = try_ffi!(unsafe { parse_str(password, "password") });

        let mut config = try_ffi!(CipherConfiguration::from_str(config));

        let result = try_ffi!(config.start_client_registration(password));

        Response::ok2(result.registration_request, result.state)
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_registration(
    config: *const c_char,
    server_setup: Buffer,
    registration_request: Buffer,
    username: *const c_char,
) -> Response {
    catch(|| {
        let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
        let server_setup = try_ffi!(unsafe { server_setup.as_slice_optional() });
        let registration_request = try_ffi!(unsafe { registration_request.as_slice() });
        let username = try_ffi!(unsafe { parse_str(username, "username") });

        let mut config = try_ffi!(CipherConfiguration::from_str(config));

        let response = try_ffi!(config.start_server_registration(
            server_setup,
            registration_request,
            username
        ));

        Response::ok2(response.registration_response, response.server_setup)
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_registration(
    config: *const c_char,
    state: Buffer,
    registration_response: Buffer,
    password: *const c_char,
) -> Response {
    catch(|| {
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
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_server_registration(
    config: *const c_char,
    registration_upload: Buffer,
) -> Response {
    catch(|| {
        let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
        let registration_upload = try_ffi!(unsafe { registration_upload.as_slice() });

        let mut config = try_ffi!(CipherConfiguration::from_str(config));

        let response = try_ffi!(config.finish_server_registration(registration_upload));
        Response::ok1(response.server_registration)
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_client_login(
    config: *const c_char,
    password: *const c_char,
) -> Response {
    catch(|| {
        let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
        let password = try_ffi!(unsafe { parse_str(password, "password") });

        let mut config = try_ffi!(CipherConfiguration::from_str(config));

        let response = try_ffi!(config.start_client_login(password));
        Response::ok2(response.credential_request, response.state)
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn start_server_login(
    config: *const c_char,
    server_setup: Buffer,
    server_registration: Buffer,
    credential_request: Buffer,
    username: *const c_char,
) -> Response {
    catch(|| {
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
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_client_login(
    config: *const c_char,
    state: Buffer,
    credential_response: Buffer,
    password: *const c_char,
) -> Response {
    catch(|| {
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
    })
}

/// # Safety
/// This function must follow the same safety rules as [parse_str] and [Buffer::as_slice].
/// The caller must ensure that the [Response] is correctly freed after use.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn finish_server_login(
    config: *const c_char,
    state: Buffer,
    credential_finalization: Buffer,
) -> Response {
    catch(|| {
        let config: &str = try_ffi!(unsafe { parse_str(config, "config") });
        let state = try_ffi!(unsafe { state.as_slice() });
        let credential_finalization = try_ffi!(unsafe { credential_finalization.as_slice() });

        let mut config = try_ffi!(CipherConfiguration::from_str(config));

        let response = try_ffi!(config.finish_server_login(state, credential_finalization));

        Response::ok1(response.session_key)
    })
}
