use std::str::FromStr;

use generic_array::{ArrayLength, GenericArray};
use opaque_ke::errors::InternalError;
use rand::SeedableRng;
use rand_chacha::ChaCha20Rng;
use serde::{Deserialize, Serialize};

use crate::Error;

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum OprfCs {
    Ristretto255,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum KeGroup {
    Ristretto255,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum KeyExchange {
    #[serde(alias = "tripleDH", alias = "triple-dh")]
    TripleDh,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", tag = "algorithm", content = "parameters")]
pub enum Ksf {
    Argon2id(Argon2id),
    __NonExhaustive(()),
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Argon2id {
    pub memory: u32,
    pub iterations: u32,
    pub parallelism: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CipherConfiguration {
    pub opaque_version: u32,
    #[serde(alias = "oprfCS")]
    pub oprf_cs: OprfCs,
    pub ke_group: KeGroup,
    pub key_exchange: KeyExchange,
    pub ksf: Ksf,

    #[serde(skip, default = "default_rng")]
    pub(crate) rng: ChaCha20Rng,
}

fn default_rng() -> ChaCha20Rng {
    ChaCha20Rng::from_entropy()
}

impl CipherConfiguration {
    pub(crate) fn fake_from_seed(seed: [u8; 32]) -> Self {
        Self {
            rng: ChaCha20Rng::from_seed(seed),
            ..Default::default()
        }
    }
}

impl Default for CipherConfiguration {
    fn default() -> Self {
        Self {
            opaque_version: 3,
            oprf_cs: OprfCs::Ristretto255,
            ke_group: KeGroup::Ristretto255,
            key_exchange: KeyExchange::TripleDh,
            ksf: Ksf::Argon2id(Argon2id {
                memory: 65536,
                iterations: 4,
                parallelism: 4,
            }),
            rng: default_rng(),
        }
    }
}

impl FromStr for CipherConfiguration {
    type Err = Error;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        serde_json::from_str(s).map_err(|e| Error::InvalidConfig(e.to_string()))
    }
}

pub(crate) struct ClientRegistrationStartResult {
    // The message is sent to the server for the next step of the registration protocol.
    pub(crate) registration_request: Vec<u8>,
    // The state is stored temporarily by the client and used in the next step of the registration protocol.
    pub(crate) state: Vec<u8>,
}

pub(crate) struct ServerRegistrationStartResult {
    pub(crate) registration_response: Vec<u8>,
    pub(crate) server_setup: Vec<u8>,
}

pub(crate) struct ClientRegistrationFinishResult {
    // The message is sent to the server for the last step of the registration protocol.
    pub(crate) registration_upload: Vec<u8>,
    pub(crate) export_key: Vec<u8>,
    pub(crate) server_s_pk: Vec<u8>,
}

pub(crate) struct ServerRegistrationFinishResult {
    pub(crate) server_registration: Vec<u8>,
}

////////////////////////////////////////

pub(crate) struct ClientLoginStartResult {
    pub(crate) credential_request: Vec<u8>,
    pub(crate) state: Vec<u8>,
}

pub(crate) struct ServerLoginStartResult {
    pub(crate) credential_response: Vec<u8>,
    pub(crate) state: Vec<u8>,
}

pub(crate) struct ClientLoginFinishResult {
    pub(crate) credential_finalization: Vec<u8>,
    pub(crate) session_key: Vec<u8>,
    pub(crate) export_key: Vec<u8>,
    pub(crate) server_s_pk: Vec<u8>,
}

pub(crate) struct ServerLoginFinishResult {
    pub(crate) session_key: Vec<u8>,
}

#[derive(Debug, Default)]
pub(crate) struct IdentityKsf {}

impl opaque_ke::ksf::Ksf for IdentityKsf {
    fn hash<L: ArrayLength<u8>>(
        &self,
        input: GenericArray<u8, L>,
    ) -> Result<GenericArray<u8, L>, InternalError> {
        Ok(input)
    }
}
