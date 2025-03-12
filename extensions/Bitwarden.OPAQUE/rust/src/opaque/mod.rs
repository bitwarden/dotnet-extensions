use argon2::Argon2;
use opaque_ke::*;
use rand::rngs::OsRng;

use crate::Error;

mod types;

pub(crate) use types::*;

// The opaque-ke crate uses a lot of generic traits, which are difficult to handle in FFI.
// This trait implements dynamic dispatch to allow using opaque-ke without generics.
pub trait OpaqueImpl {
    fn start_client_registration(
        &self,
        password: &str,
    ) -> Result<types::ClientRegistrationStartResult, Error>;
    fn start_server_registration(
        &self,
        server_setup: Option<&[u8]>,
        registration_request: &[u8],
        username: &str,
    ) -> Result<types::ServerRegistrationStartResult, Error>;
    fn finish_client_registration(
        &self,
        state: &[u8],
        registration_response: &[u8],
        password: &str,
    ) -> Result<types::ClientRegistrationFinishResult, Error>;
    fn finish_server_registration(
        &self,
        registration_upload: &[u8],
    ) -> Result<types::ServerRegistrationFinishResult, Error>;

    fn start_client_login(&self, password: &str) -> Result<types::ClientLoginStartResult, Error>;
    fn start_server_login(
        &self,
        server_setup: &[u8],
        server_registration: &[u8],
        credential_request: &[u8],
        username: &str,
    ) -> Result<types::ServerLoginStartResult, Error>;
    fn finish_client_login(
        &self,
        state: &[u8],
        credential_response: &[u8],
        password: &str,
    ) -> Result<types::ClientLoginFinishResult, Error>;
    fn finish_server_login(
        &self,
        state: &[u8],
        credential_finalization: &[u8],
    ) -> Result<types::ServerLoginFinishResult, Error>;
}

// This trait exists to extract the differences between all the OpaqueImpl implementations.
// This would allow replacing those impls by a macro in the future.
pub trait OpaqueUtil: Sized {
    type Output;
    fn as_variant(config: &CipherConfiguration) -> Option<Self>;
    fn get_ksf(&self) -> Result<Self::Output, Error>;
}

// Implement the OpaqueImpl trait for the CipherConfiguration enum, which allows us to dynamically dispatch to the correct cipher suite.
impl OpaqueImpl for CipherConfiguration {
    fn start_client_registration(
        &self,
        password: &str,
    ) -> Result<types::ClientRegistrationStartResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.start_client_registration(password);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn start_server_registration(
        &self,
        server_setup: Option<&[u8]>,
        registration_request: &[u8],
        username: &str,
    ) -> Result<types::ServerRegistrationStartResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.start_server_registration(server_setup, registration_request, username);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn finish_client_registration(
        &self,
        state: &[u8],
        registration_response: &[u8],
        password: &str,
    ) -> Result<types::ClientRegistrationFinishResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.finish_client_registration(state, registration_response, password);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn finish_server_registration(
        &self,
        registration_upload: &[u8],
    ) -> Result<types::ServerRegistrationFinishResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.finish_server_registration(registration_upload);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }

    fn start_client_login(&self, password: &str) -> Result<types::ClientLoginStartResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.start_client_login(password);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn start_server_login(
        &self,
        server_setup: &[u8],
        server_registration: &[u8],
        credential_request: &[u8],
        username: &str,
    ) -> Result<types::ServerLoginStartResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.start_server_login(
                server_setup,
                server_registration,
                credential_request,
                username,
            );
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn finish_client_login(
        &self,
        state: &[u8],
        credential_response: &[u8],
        password: &str,
    ) -> Result<types::ClientLoginFinishResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.finish_client_login(state, credential_response, password);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
    fn finish_server_login(
        &self,
        state: &[u8],
        credential_finalization: &[u8],
    ) -> Result<types::ServerLoginFinishResult, Error> {
        if let Some(suite) = RistrettoTripleDhArgonSuite::as_variant(self) {
            return suite.finish_server_login(state, credential_finalization);
        };
        Err(Error::InvalidInput("Invalid cipher configuration"))
    }
}

// Define the cipher suite and implement the required traits on it (OpaqueImpl+OpaqueKsf+opaque_ke::CipherSuite)
struct RistrettoTripleDhArgonSuite(Argon2id);
impl opaque_ke::CipherSuite for RistrettoTripleDhArgonSuite {
    type OprfCs = opaque_ke::Ristretto255;
    type KeGroup = opaque_ke::Ristretto255;
    type KeyExchange = opaque_ke::key_exchange::tripledh::TripleDh;
    type Ksf = argon2::Argon2<'static>;
}
impl OpaqueUtil for RistrettoTripleDhArgonSuite {
    type Output = argon2::Argon2<'static>;

    fn as_variant(config: &CipherConfiguration) -> Option<Self> {
        match config {
            CipherConfiguration {
                oprf_cs: OprfCs::Ristretto255,
                ke_group: KeGroup::Ristretto255,
                key_exchange: KeyExchange::TripleDh,
                ksf: Ksf::Argon2id(argon),
            } => Some(Self(*argon)),
            _ => None,
        }
    }
    fn get_ksf(&self) -> Result<Self::Output, Error> {
        Ok(Argon2::new(
            argon2::Algorithm::Argon2id,
            argon2::Version::V0x13,
            argon2::Params::new(
                self.0.memory_kib,
                self.0.iterations,
                self.0.parallelism,
                None,
            )
            .map_err(|_| Error::InvalidInput("Invalid Argon2 parameters"))?,
        ))
    }
}

// This implementation will be identical between any cipher suite, but we can't simply reuse it because of all the generic bounds on the CipherSuite trait.
// If we need to add more cipher suites, we will need to copy this implementation over, or ideally use a macro to generate it.
impl OpaqueImpl for RistrettoTripleDhArgonSuite {
    fn start_client_registration(
        &self,
        password: &str,
    ) -> Result<types::ClientRegistrationStartResult, Error> {
        let result = ClientRegistration::<Self>::start(&mut OsRng, password.as_bytes())?;
        Ok(types::ClientRegistrationStartResult {
            registration_request: result.message.serialize().to_vec(),
            state: result.state.serialize().to_vec(),
        })
    }
    fn start_server_registration(
        &self,
        server_setup: Option<&[u8]>,
        registration_request: &[u8],
        username: &str,
    ) -> Result<types::ServerRegistrationStartResult, Error> {
        let server_setup = match server_setup {
            Some(server_setup) => ServerSetup::<Self>::deserialize(server_setup)?,
            None => ServerSetup::<Self>::new(&mut OsRng),
        };
        let result = ServerRegistration::start(
            &server_setup,
            RegistrationRequest::deserialize(registration_request)?,
            username.as_bytes(),
        )?;
        Ok(types::ServerRegistrationStartResult {
            registration_response: result.message.serialize().to_vec(),
            server_setup: server_setup.serialize().to_vec(),
        })
    }
    fn finish_client_registration(
        &self,
        state: &[u8],
        registration_response: &[u8],
        password: &str,
    ) -> Result<types::ClientRegistrationFinishResult, Error> {
        let state = ClientRegistration::<Self>::deserialize(state)?;
        let result = state.finish(
            &mut OsRng,
            password.as_bytes(),
            RegistrationResponse::deserialize(registration_response)?,
            ClientRegistrationFinishParameters::new(Identifiers::default(), Some(&self.get_ksf()?)),
        )?;
        Ok(types::ClientRegistrationFinishResult {
            registration_upload: result.message.serialize().to_vec(),
            export_key: result.export_key.to_vec(),
            server_s_pk: result.server_s_pk.serialize().to_vec(),
        })
    }
    fn finish_server_registration(
        &self,
        registration_upload: &[u8],
    ) -> Result<types::ServerRegistrationFinishResult, Error> {
        let registration = ServerRegistration::finish(RegistrationUpload::<Self>::deserialize(
            registration_upload,
        )?);
        Ok(types::ServerRegistrationFinishResult {
            server_registration: registration.serialize().to_vec(),
        })
    }

    fn start_client_login(&self, password: &str) -> Result<types::ClientLoginStartResult, Error> {
        let result = ClientLogin::<Self>::start(&mut OsRng, password.as_bytes())?;
        Ok(types::ClientLoginStartResult {
            credential_request: result.message.serialize().to_vec(),
            state: result.state.serialize().to_vec(),
        })
    }
    fn start_server_login(
        &self,
        server_setup: &[u8],
        server_registration: &[u8],
        credential_request: &[u8],
        username: &str,
    ) -> Result<types::ServerLoginStartResult, Error> {
        let result = ServerLogin::start(
            &mut OsRng,
            &ServerSetup::<Self>::deserialize(server_setup)?,
            Some(ServerRegistration::<Self>::deserialize(
                server_registration,
            )?),
            CredentialRequest::deserialize(credential_request)?,
            username.as_bytes(),
            ServerLoginStartParameters::default(),
        )?;
        Ok(types::ServerLoginStartResult {
            credential_response: result.message.serialize().to_vec(),
            state: result.state.serialize().to_vec(),
        })
    }
    fn finish_client_login(
        &self,
        state: &[u8],
        credential_response: &[u8],
        password: &str,
    ) -> Result<types::ClientLoginFinishResult, Error> {
        let client_login = ClientLogin::<Self>::deserialize(state)?;
        let result = client_login.finish(
            password.as_bytes(),
            CredentialResponse::deserialize(credential_response)?,
            ClientLoginFinishParameters::new(None, Identifiers::default(), Some(&self.get_ksf()?)),
        )?;
        Ok(types::ClientLoginFinishResult {
            credential_finalization: result.message.serialize().to_vec(),
            session_key: result.session_key.to_vec(),
            export_key: result.export_key.to_vec(),
            server_s_pk: result.server_s_pk.serialize().to_vec(),
        })
    }
    fn finish_server_login(
        &self,
        state: &[u8],
        credential_finalization: &[u8],
    ) -> Result<types::ServerLoginFinishResult, Error> {
        let server_login = ServerLogin::<Self>::deserialize(state)?;
        let result = server_login.finish(CredentialFinalization::deserialize(
            credential_finalization,
        )?)?;
        Ok(types::ServerLoginFinishResult {
            session_key: result.session_key.to_vec(),
        })
    }
}

/*
    TODO: If in the future we add more cipher suites, we will need to create
    a new OpaqueImpl implementation and add it to the CipherConfiguration matches.
    This will require a lot of duplication, but can be simplified with a macro as follows:

    macro_rules! implement_cipher_suites {
        (  $shared_type:ident; $( $name:ident : $pat:pat => $cipher:expr );+  ) => {
            // Check that any type implements the required traits
            const fn _assert_opaque_ksf_and_cipher_suite<T: OpaqueKsf + opaque_ke::CipherSuite>() {}
            const _: () = { $( _assert_opaque_ksf_and_cipher_suite::<$name>(); )+ };

            // Implement OpaqueImpl for the shared type, and dispatch to the correct cipher suite
            impl OpaqueImpl for $shared_type {
                fn start_client_registration(&self, password: &str) -> Result<types::ClientRegistrationStartResult, Error> {
                    $(if let Some(suite) = $name::as_variant(self) {
                        return suite.start_client_registration(password);
                    };)+
                    Err(Error::InvalidInput("Invalid cipher configuration"))
                }
                ...
            }

            // Implement OpaqueImpl for each cipher suite.
            // This is just copying the implementation on RistrettoTripleDhArgonSuite and wrapping it in $()+
            $(impl OpaqueImpl for $name {
                fn start_client_registration(&self, password: &str) -> Result<types::ClientRegistrationStartResult, Error> {
                    let result = ClientRegistration::<Self>::start(&mut OsRng, password.as_bytes())?;
                    Ok(types::ClientRegistrationStartResult {
                        registration_request: result.message.serialize().to_vec(),
                        state: result.state.serialize().to_vec(),
                    })
                }
                ...
            })+
        };
    }

    implement_cipher_suites! {
        CipherConfiguration;
        RistrettoTripleDhArgonSuite: CipherConfiguration {
            oprf_cs: OprfCs::Ristretto255,
            ke_group: KeGroup::Ristretto255,
            key_exchange: KeyExchange::TripleDh,
            ksf: Ksf::Argon2id(argon),
        } => RistrettoTripleDhArgonSuite(*argon)
    }
*/
