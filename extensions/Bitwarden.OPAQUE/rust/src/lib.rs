#![forbid(unsafe_op_in_unsafe_fn)]

use zeroizing_alloc::ZeroAlloc;

#[global_allocator]
static ALLOC: ZeroAlloc<std::alloc::System> = ZeroAlloc(std::alloc::System);

mod client;
mod ffi;
mod ffi_types;
mod server;

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

// These type aliases were copied from the `opaque_ke` crate, as they are not public, but help with the type signatures.
pub(crate) type OutputSize<D> =
    <<D as digest::core_api::CoreProxy>::Core as digest::OutputSizeUser>::OutputSize;
pub(crate) type OprfGroup<CS> =
    <<CS as opaque_ke::CipherSuite>::OprfCs as voprf::CipherSuite>::Group;
pub(crate) type OprfHash<CS> = <<CS as opaque_ke::CipherSuite>::OprfCs as voprf::CipherSuite>::Hash;
type NonceLen = digest::consts::U32;
pub(crate) type EnvelopeLen<CS> = digest::typenum::Sum<NonceLen, OutputSize<OprfHash<CS>>>;
pub(crate) type ClientRegistrationLen<CS> = digest::typenum::Sum<
    <OprfGroup<CS> as voprf::Group>::ScalarLen,
    <OprfGroup<CS> as voprf::Group>::ElemLen,
>;
pub(crate) type ServerSetupLen<CS, S> = digest::typenum::Sum<
    digest::typenum::Sum<
        OutputSize<OprfHash<CS>>,
        <S as opaque_ke::keypair::SecretKey<<CS as opaque_ke::CipherSuite>::KeGroup>>::Len,
    >,
    <<CS as opaque_ke::CipherSuite>::KeGroup as opaque_ke::key_exchange::group::KeGroup>::SkLen,
>;

pub struct DefaultCipherSuite;

impl opaque_ke::CipherSuite for DefaultCipherSuite {
    type OprfCs = opaque_ke::Ristretto255;
    type KeGroup = opaque_ke::Ristretto255;
    type KeyExchange = opaque_ke::key_exchange::tripledh::TripleDh;

    type Ksf = argon2::Argon2<'static>;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test() {
        let password = "password";
        let username = "username";

        let registration_request =
            client::start_client_registration::<DefaultCipherSuite>(password).unwrap();

        let server_start_result = server::start_server_registration::<DefaultCipherSuite>(
            &registration_request.registration_request,
            username,
        )
        .unwrap();

        let client_finish_result = client::finish_client_registration::<DefaultCipherSuite>(
            &registration_request.state,
            &server_start_result.registration_response,
            password,
        )
        .unwrap();

        let server_finish_result =
            server::finish_server_registration::<DefaultCipherSuite>(&client_finish_result.registration_upload)
                .unwrap();

        let _ = server_finish_result;
    }
}
