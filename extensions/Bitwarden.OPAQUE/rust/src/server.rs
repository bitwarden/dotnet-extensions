use std::ops::Add;

use digest::typenum::Sum;
use generic_array::ArrayLength;
use opaque_ke::{CipherSuite, key_exchange::group::KeGroup, keypair::PrivateKey, *};
use rand::rngs::OsRng;
use voprf::Group;

use crate::*;

pub(crate) struct ServerRegistrationStartResult {
    pub(crate) message: Vec<u8>,
    pub(crate) server_setup: Vec<u8>,
}

pub(crate) fn start_server_registration<CS: CipherSuite>(
    registration_request_bytes: &[u8],
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
        RegistrationRequest::deserialize(registration_request_bytes)?,
        username.as_bytes(),
    )?;

    Ok(ServerRegistrationStartResult {
        message: result.message.serialize().to_vec(),
        server_setup: server_setup.serialize().to_vec(),
    })
}

pub(crate) fn finish_server_registration<CS: CipherSuite>(
    registration_upload_bytes: &[u8],
) -> Result<Vec<u8>, Error>
where
    NonceLen: Add<OutputSize<OprfHash<CS>>>,
    EnvelopeLen<CS>: ArrayLength<u8>,
    <CS::KeGroup as KeGroup>::PkLen: Add<OutputSize<OprfHash<CS>>>,
    Sum<<CS::KeGroup as KeGroup>::PkLen, OutputSize<OprfHash<CS>>>:
        ArrayLength<u8> + Add<EnvelopeLen<CS>>,
    RegistrationUploadLen<CS>: ArrayLength<u8>,
{
    let registration = ServerRegistration::finish(RegistrationUpload::<CS>::deserialize(
        registration_upload_bytes,
    )?);
    Ok(registration.serialize().to_vec())
}
