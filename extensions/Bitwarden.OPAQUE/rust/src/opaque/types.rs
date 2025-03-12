#[derive(Debug, Clone, Copy)]
pub enum OprfCs {
    Ristretto255,
}

#[derive(Debug, Clone, Copy)]
pub enum KeGroup {
    Ristretto255,
}

#[derive(Debug, Clone, Copy)]
pub enum KeyExchange {
    TripleDh,
}

#[derive(Debug, Clone, Copy)]
pub enum Ksf {
    Argon2id(Argon2id),
}

#[derive(Debug, Clone, Copy)]
pub struct Argon2id {
    pub memory_kib: u32,
    pub iterations: u32,
    pub parallelism: u32,
}

#[derive(Debug, Clone, Copy)]
pub struct CipherConfiguration {
    pub oprf_cs: OprfCs,
    pub ke_group: KeGroup,
    pub key_exchange: KeyExchange,
    pub ksf: Ksf,
}

impl Default for CipherConfiguration {
    fn default() -> Self {
        Self {
            oprf_cs: OprfCs::Ristretto255,
            ke_group: KeGroup::Ristretto255,
            key_exchange: KeyExchange::TripleDh,
            ksf: Ksf::Argon2id(Argon2id {
                memory_kib: 64,
                iterations: 3,
                parallelism: 1,
            }),
        }
    }
}

pub(crate) struct ClientRegistrationStartResult {
    // The message is sent to the server for the next step of the registration protocol.
    pub(crate) registration_request: Vec<u8>,
    // The state is stored temporarily by the client and used in the next step of the registration protocol.
    pub(crate) state: Vec<u8>,
}

pub(crate) struct ClientRegistrationFinishResult {
    // The message is sent to the server for the last step of the registration protocol.
    pub(crate) registration_upload: Vec<u8>,
    pub(crate) export_key: Vec<u8>,
    pub(crate) server_s_pk: Vec<u8>,
}

pub(crate) struct ServerRegistrationStartResult {
    pub(crate) registration_response: Vec<u8>,
    pub(crate) server_setup: Vec<u8>,
}

pub(crate) struct ServerRegistrationFinishResult {
    pub(crate) server_registration: Vec<u8>,
}
