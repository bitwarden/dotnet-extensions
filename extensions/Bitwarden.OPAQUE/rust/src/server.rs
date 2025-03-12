use std::ops::Add;

use digest::typenum::Sum;
use generic_array::ArrayLength;
use opaque_ke::{CipherSuite, key_exchange::group::KeGroup, keypair::PrivateKey, *};
use rand::rngs::OsRng;
use voprf::Group;

use crate::*;

pub(crate) struct ServerRegistrationStartResult {
    pub(crate) registration_response: Vec<u8>,
    pub(crate) server_setup: Vec<u8>,
}

pub(crate) fn start_server_registration<CS: CipherSuite>(
    registration_request: &[u8],
    username: &str,
) -> Result<ServerRegistrationStartResult, Error>
where
    // Message::Serialize
    <OprfGroup<CS> as Group>::ElemLen: Add<<CS::KeGroup as KeGroup>::PkLen>,
    RegistrationResponseLen<CS>: ArrayLength<u8>,
    // ServerSetup::Serialize
    OutputSize<OprfHash<CS>>: Add<<<CS as CipherSuite>::KeGroup as KeGroup>::SkLen>,
    Sum<OutputSize<OprfHash<CS>>, <<CS as CipherSuite>::KeGroup as KeGroup>::SkLen>:
        ArrayLength<u8> + Add<<CS::KeGroup as KeGroup>::SkLen>,
    ServerSetupLen<CS, PrivateKey<CS::KeGroup>>: ArrayLength<u8>,
{
    let server_setup = ServerSetup::<CS>::new(&mut OsRng);

    let result = ServerRegistration::start(
        &server_setup,
        RegistrationRequest::deserialize(registration_request)?,
        username.as_bytes(),
    )?;

    Ok(ServerRegistrationStartResult {
        registration_response: result.message.serialize().to_vec(),
        server_setup: server_setup.serialize().to_vec(),
    })
}

pub(crate) struct ServerRegistrationFinishResult {
    pub(crate) server_registration: Vec<u8>,
}

pub(crate) fn finish_server_registration<CS: CipherSuite>(
    registration_upload: &[u8],
) -> Result<ServerRegistrationFinishResult, Error>
where
    NonceLen: Add<OutputSize<OprfHash<CS>>>,
    EnvelopeLen<CS>: ArrayLength<u8>,
    <CS::KeGroup as KeGroup>::PkLen: Add<OutputSize<OprfHash<CS>>>,
    Sum<<CS::KeGroup as KeGroup>::PkLen, OutputSize<OprfHash<CS>>>:
        ArrayLength<u8> + Add<EnvelopeLen<CS>>,
    RegistrationUploadLen<CS>: ArrayLength<u8>,
{
    let registration =
        ServerRegistration::finish(RegistrationUpload::<CS>::deserialize(registration_upload)?);
    Ok(ServerRegistrationFinishResult {
        server_registration: registration.serialize().to_vec(),
    })
}

/*
pub(crate) struct ServerLoginStartResult {}

pub(crate) fn start_server_login<CS: CipherSuite>(
    login_start: &[u8],
    username: &str,
) -> Result<ServerLoginStartResult, Error>
where

{


  //  let result = ServerLogin::start(LoginStart::deserialize(login_start)?, username.as_bytes())?;
  (())  Ok(ServerLoginStartResult {})
}
*/
