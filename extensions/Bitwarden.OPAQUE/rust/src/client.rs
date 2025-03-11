use std::ops::Add;

use digest::typenum::Sum;
use generic_array::ArrayLength;
use opaque_ke::{key_exchange::group::KeGroup, *};
use rand::rngs::OsRng;
use voprf::Group;

use crate::*;

pub(crate) struct ClientRegistrationStartResult {
    // The message is sent to the server for the next step of the registration protocol.
    pub(crate) message: Vec<u8>,
    // The state is stored temporarily by the client and used in the next step of the registration protocol.
    pub(crate) state: Vec<u8>,
}

pub(crate) fn start_client_registration<CS: CipherSuite>(
    password: &str,
) -> Result<ClientRegistrationStartResult, Error>
where
    <OprfGroup<CS> as Group>::ScalarLen: Add<<OprfGroup<CS> as Group>::ElemLen>,
    ClientRegistrationLen<CS>: ArrayLength<u8>,
{
    let result = ClientRegistration::<CS>::start(&mut OsRng, password.as_bytes())?;

    Ok(ClientRegistrationStartResult {
        message: result.message.serialize().to_vec(),
        state: result.state.serialize().to_vec(),
    })
}

pub(crate) struct ClientRegistrationFinishResult {
    // The message is sent to the server for the last step of the registration protocol.
    pub(crate) message: Vec<u8>,
    pub(crate) export_key: Vec<u8>,
    pub(crate) server_s_pk: Vec<u8>,
}

pub(crate) fn finish_client_registration<CS: CipherSuite>(
    state: &[u8],
    registration_response_bytes: &[u8],
    password: &str,
) -> Result<ClientRegistrationFinishResult, Error>
where
    NonceLen: Add<OutputSize<OprfHash<CS>>>,
    EnvelopeLen<CS>: ArrayLength<u8>,
    <CS::KeGroup as KeGroup>::PkLen: Add<OutputSize<OprfHash<CS>>>,
    Sum<<CS::KeGroup as KeGroup>::PkLen, OutputSize<OprfHash<CS>>>:
        ArrayLength<u8> + Add<EnvelopeLen<CS>>,
    RegistrationUploadLen<CS>: ArrayLength<u8>,
{
    let state = ClientRegistration::<CS>::deserialize(state)?;

    let result = state.finish(
        &mut OsRng,
        password.as_bytes(),
        RegistrationResponse::deserialize(registration_response_bytes)?,
        ClientRegistrationFinishParameters::default(),
    )?;

    Ok(ClientRegistrationFinishResult {
        message: result.message.serialize().to_vec(),
        export_key: result.export_key.to_vec(),
        server_s_pk: result.server_s_pk.serialize().to_vec(),
    })
}
