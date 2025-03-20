use opaque_ke::*;
use rand_chacha::ChaCha20Rng;

use crate::Error;

mod types;

pub(crate) use types::*;

// This trait exists to extract the differences between all the OpaqueImpl implementations.
pub trait OpaqueUtil<'a>: opaque_ke::CipherSuite + OpaqueImpl + Sized {
    fn as_variant(config: &'a mut CipherConfiguration) -> Option<Self>;
    fn get_ksf(&self) -> Result<Self::Ksf, Error>;
    fn get_rng(&mut self) -> &mut ChaCha20Rng;
}

fn invalid_config(config: &CipherConfiguration) -> Error {
    Error::InvalidConfig(serde_json::to_string(config).unwrap_or_default())
}

// Define the cipher suites and implement the required traits on them (opaque_ke::CipherSuite+OpaqueUtil)
struct RistrettoTripleDhArgonSuite<'a>(&'a mut ChaCha20Rng, Argon2id);
impl opaque_ke::CipherSuite for RistrettoTripleDhArgonSuite<'_> {
    type OprfCs = opaque_ke::Ristretto255;
    type KeGroup = opaque_ke::Ristretto255;
    type KeyExchange = opaque_ke::key_exchange::tripledh::TripleDh;
    type Ksf = argon2::Argon2<'static>;
}
impl<'a> OpaqueUtil<'a> for RistrettoTripleDhArgonSuite<'a> {
    fn as_variant(config: &'a mut CipherConfiguration) -> Option<Self> {
        match config {
            CipherConfiguration {
                opaque_version: 3,
                oprf_cs: OprfCs::Ristretto255,
                ke_group: KeGroup::Ristretto255,
                key_exchange: KeyExchange::TripleDh,
                ksf: types::Ksf::Argon2id(argon),
                rng,
            } => Some(Self(rng, *argon)),
            _ => None,
        }
    }
    fn get_ksf(&self) -> Result<Self::Ksf, Error> {
        Ok(argon2::Argon2::new(
            argon2::Algorithm::Argon2id,
            argon2::Version::V0x13,
            argon2::Params::new(self.1.memory, self.1.iterations, self.1.parallelism, None)
                .map_err(|_| Error::InvalidConfig("Invalid Argon2 parameters".into()))?,
        ))
    }

    fn get_rng(&mut self) -> &mut ChaCha20Rng {
        self.0
    }
}

struct RistrettoTripleDhIdentitySuite<'a>(&'a mut ChaCha20Rng);
impl opaque_ke::CipherSuite for RistrettoTripleDhIdentitySuite<'_> {
    type OprfCs = opaque_ke::Ristretto255;
    type KeGroup = opaque_ke::Ristretto255;
    type KeyExchange = opaque_ke::key_exchange::tripledh::TripleDh;
    type Ksf = ksf::Identity;
}
impl<'a> OpaqueUtil<'a> for RistrettoTripleDhIdentitySuite<'a> {
    fn as_variant(config: &'a mut CipherConfiguration) -> Option<Self> {
        match config {
            CipherConfiguration {
                opaque_version: 3,
                oprf_cs: OprfCs::Ristretto255,
                ke_group: KeGroup::Ristretto255,
                key_exchange: KeyExchange::TripleDh,
                ksf: types::Ksf::Identity,
                rng,
            } => Some(Self(rng)),
            _ => None,
        }
    }
    fn get_ksf(&self) -> Result<Self::Ksf, Error> {
        Ok(ksf::Identity)
    }

    fn get_rng(&mut self) -> &mut ChaCha20Rng {
        self.0
    }
}

// This generic utility function is used to dynamically dispatch to the correct cipher suite
fn with_variants<T>(
    config: &mut CipherConfiguration,
    func: impl FnOnce(&mut dyn OpaqueImpl) -> Result<T, Error>,
) -> Result<T, Error> {
    if let Some(mut suite) = RistrettoTripleDhArgonSuite::as_variant(config) {
        return func(&mut suite);
    };
    if let Some(mut suite) = RistrettoTripleDhIdentitySuite::as_variant(config) {
        return func(&mut suite);
    };
    Err(invalid_config(config))
}

pub fn register_seeded_fake_config(seed: [u8; 32]) -> Result<(Vec<u8>, Vec<u8>), Error> {
    use rand::RngCore as _;

    let mut config = CipherConfiguration::fake_from_seed(seed);

    let mut password: [u8; 32] = [0; 32];
    let mut username: [u8; 32] = [0; 32];
    config.rng.fill_bytes(&mut password);
    config.rng.fill_bytes(&mut username);
    let password = hex::encode(password);
    let username = hex::encode(username);

    let start = config.start_client_registration(password.as_str())?;
    let server_start =
        config.start_server_registration(None, &start.registration_request, &username)?;
    let client_finish = config.finish_client_registration(
        &start.state,
        &server_start.registration_response,
        &password,
    )?;
    let server_finish = config.finish_server_registration(&client_finish.registration_upload)?;
    Ok((server_start.server_setup, server_finish.server_registration))
}

// The opaque-ke crate uses a lot of generic traits, which are difficult to handle in FFI.
// This trait implements dynamic dispatch to allow using opaque-ke without generics.
pub trait OpaqueImpl {
    fn start_client_registration(
        &mut self,
        password: &str,
    ) -> Result<types::ClientRegistrationStartResult, Error>;
    fn start_server_registration(
        &mut self,
        server_setup: Option<&[u8]>,
        registration_request: &[u8],
        username: &str,
    ) -> Result<types::ServerRegistrationStartResult, Error>;
    fn finish_client_registration(
        &mut self,
        state: &[u8],
        registration_response: &[u8],
        password: &str,
    ) -> Result<types::ClientRegistrationFinishResult, Error>;
    fn finish_server_registration(
        &mut self,
        registration_upload: &[u8],
    ) -> Result<types::ServerRegistrationFinishResult, Error>;

    fn start_client_login(
        &mut self,
        password: &str,
    ) -> Result<types::ClientLoginStartResult, Error>;
    fn start_server_login(
        &mut self,
        server_setup: &[u8],
        server_registration: &[u8],
        credential_request: &[u8],
        username: &str,
    ) -> Result<types::ServerLoginStartResult, Error>;
    fn finish_client_login(
        &mut self,
        state: &[u8],
        credential_response: &[u8],
        password: &str,
    ) -> Result<types::ClientLoginFinishResult, Error>;
    fn finish_server_login(
        &mut self,
        state: &[u8],
        credential_finalization: &[u8],
    ) -> Result<types::ServerLoginFinishResult, Error>;
}

// Implement OpaqueImpl for the shared type, and dynamically dispatch to the correct cipher suite
impl OpaqueImpl for CipherConfiguration {
    fn start_client_registration(
        &mut self,
        password: &str,
    ) -> Result<types::ClientRegistrationStartResult, Error> {
        with_variants(self, |suite| suite.start_client_registration(password))
    }
    fn start_server_registration(
        &mut self,
        server_setup: Option<&[u8]>,
        registration_request: &[u8],
        username: &str,
    ) -> Result<types::ServerRegistrationStartResult, Error> {
        with_variants(self, |suite| {
            suite.start_server_registration(server_setup, registration_request, username)
        })
    }
    fn finish_client_registration(
        &mut self,
        state: &[u8],
        registration_response: &[u8],
        password: &str,
    ) -> Result<types::ClientRegistrationFinishResult, Error> {
        with_variants(self, |suite| {
            suite.finish_client_registration(state, registration_response, password)
        })
    }
    fn finish_server_registration(
        &mut self,
        registration_upload: &[u8],
    ) -> Result<types::ServerRegistrationFinishResult, Error> {
        with_variants(self, |suite| {
            suite.finish_server_registration(registration_upload)
        })
    }

    fn start_client_login(
        &mut self,
        password: &str,
    ) -> Result<types::ClientLoginStartResult, Error> {
        with_variants(self, |suite| suite.start_client_login(password))
    }
    fn start_server_login(
        &mut self,
        server_setup: &[u8],
        server_registration: &[u8],
        credential_request: &[u8],
        username: &str,
    ) -> Result<types::ServerLoginStartResult, Error> {
        with_variants(self, |suite| {
            suite.start_server_login(
                server_setup,
                server_registration,
                credential_request,
                username,
            )
        })
    }
    fn finish_client_login(
        &mut self,
        state: &[u8],
        credential_response: &[u8],
        password: &str,
    ) -> Result<types::ClientLoginFinishResult, Error> {
        with_variants(self, |suite| {
            suite.finish_client_login(state, credential_response, password)
        })
    }
    fn finish_server_login(
        &mut self,
        state: &[u8],
        credential_finalization: &[u8],
    ) -> Result<types::ServerLoginFinishResult, Error> {
        with_variants(self, |suite| {
            suite.finish_server_login(state, credential_finalization)
        })
    }
}

// Implement OpaqueImpl for each cipher suite. The code is entirely the same except for the impl OpaqueImpl for <type>
macro_rules! implement_cipher_suite {
    ( $type:ty ) => {
        impl crate::opaque::OpaqueImpl for $type {
            fn start_client_registration(
                &mut self,
                password: &str,
            ) -> Result<crate::opaque::types::ClientRegistrationStartResult, Error> {
                let result =
                    ClientRegistration::<Self>::start(self.get_rng(), password.as_bytes())?;
                Ok(crate::opaque::types::ClientRegistrationStartResult {
                    registration_request: result.message.serialize().to_vec(),
                    state: result.state.serialize().to_vec(),
                })
            }
            fn start_server_registration(
                &mut self,
                server_setup: Option<&[u8]>,
                registration_request: &[u8],
                username: &str,
            ) -> Result<crate::opaque::types::ServerRegistrationStartResult, Error> {
                let server_setup = match server_setup {
                    Some(server_setup) => ServerSetup::<Self>::deserialize(server_setup)?,
                    None => ServerSetup::<Self>::new(self.get_rng()),
                };
                let result = ServerRegistration::start(
                    &server_setup,
                    RegistrationRequest::deserialize(registration_request)?,
                    username.as_bytes(),
                )?;
                Ok(crate::opaque::types::ServerRegistrationStartResult {
                    registration_response: result.message.serialize().to_vec(),
                    server_setup: server_setup.serialize().to_vec(),
                })
            }
            fn finish_client_registration(
                &mut self,
                state: &[u8],
                registration_response: &[u8],
                password: &str,
            ) -> Result<crate::opaque::types::ClientRegistrationFinishResult, Error> {
                let state = ClientRegistration::<Self>::deserialize(state)?;
                let ksf = self.get_ksf()?;
                let response = RegistrationResponse::deserialize(registration_response)?;
                let params =
                    ClientRegistrationFinishParameters::new(Identifiers::default(), Some(&ksf));
                let result = state.finish(self.get_rng(), password.as_bytes(), response, params)?;
                Ok(crate::opaque::types::ClientRegistrationFinishResult {
                    registration_upload: result.message.serialize().to_vec(),
                    export_key: result.export_key.to_vec(),
                    server_s_pk: result.server_s_pk.serialize().to_vec(),
                })
            }
            fn finish_server_registration(
                &mut self,
                registration_upload: &[u8],
            ) -> Result<crate::opaque::types::ServerRegistrationFinishResult, Error> {
                let upload = RegistrationUpload::<Self>::deserialize(registration_upload)?;
                let registration = ServerRegistration::finish(upload);
                Ok(crate::opaque::types::ServerRegistrationFinishResult {
                    server_registration: registration.serialize().to_vec(),
                })
            }

            fn start_client_login(
                &mut self,
                password: &str,
            ) -> Result<crate::opaque::types::ClientLoginStartResult, Error> {
                let result = ClientLogin::<Self>::start(self.get_rng(), password.as_bytes())?;
                Ok(crate::opaque::types::ClientLoginStartResult {
                    credential_request: result.message.serialize().to_vec(),
                    state: result.state.serialize().to_vec(),
                })
            }
            fn start_server_login(
                &mut self,
                server_setup: &[u8],
                server_registration: &[u8],
                credential_request: &[u8],
                username: &str,
            ) -> Result<crate::opaque::types::ServerLoginStartResult, Error> {
                let server_setup = ServerSetup::<Self>::deserialize(server_setup)?;
                let server_registration =
                    ServerRegistration::<Self>::deserialize(server_registration)?;
                let credential_request = CredentialRequest::deserialize(credential_request)?;

                let result = ServerLogin::start(
                    self.get_rng(),
                    &server_setup,
                    Some(server_registration),
                    credential_request,
                    username.as_bytes(),
                    ServerLoginStartParameters::default(),
                )?;
                Ok(crate::opaque::types::ServerLoginStartResult {
                    credential_response: result.message.serialize().to_vec(),
                    state: result.state.serialize().to_vec(),
                })
            }
            fn finish_client_login(
                &mut self,
                state: &[u8],
                credential_response: &[u8],
                password: &str,
            ) -> Result<crate::opaque::types::ClientLoginFinishResult, Error> {
                let client_login = ClientLogin::<Self>::deserialize(state)?;
                let ksf = self.get_ksf()?;
                let params =
                    ClientLoginFinishParameters::new(None, Identifiers::default(), Some(&ksf));
                let result = client_login.finish(
                    password.as_bytes(),
                    CredentialResponse::deserialize(credential_response)?,
                    params,
                )?;
                Ok(crate::opaque::types::ClientLoginFinishResult {
                    credential_finalization: result.message.serialize().to_vec(),
                    session_key: result.session_key.to_vec(),
                    export_key: result.export_key.to_vec(),
                    server_s_pk: result.server_s_pk.serialize().to_vec(),
                })
            }
            fn finish_server_login(
                &mut self,
                state: &[u8],
                credential_finalization: &[u8],
            ) -> Result<crate::opaque::types::ServerLoginFinishResult, Error> {
                let server_login = ServerLogin::<Self>::deserialize(state)?;
                let result = server_login.finish(CredentialFinalization::deserialize(
                    credential_finalization,
                )?)?;
                Ok(crate::opaque::types::ServerLoginFinishResult {
                    session_key: result.session_key.to_vec(),
                })
            }
        }
    };
}

implement_cipher_suite!(RistrettoTripleDhArgonSuite<'_>);
implement_cipher_suite!(RistrettoTripleDhIdentitySuite<'_>);
