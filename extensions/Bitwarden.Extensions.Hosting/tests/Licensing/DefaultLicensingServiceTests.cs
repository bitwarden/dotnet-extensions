using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using Bitwarden.Extensions.Hosting.Licensing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace Bitwarden.Extensions.Hosting.Tests.Licensing;

public class DefaultLicensingServiceTests
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly FakeTimeProvider _fakeTimeProvider;

    public DefaultLicensingServiceTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    // Created using:
    // openssl req -x509 -newkey rsa:4096 -sha256 -nodes -keyout test.key -out test.crt -subj "/CN=LicenseTest" -days 3650
    // openssl pkcs12 -export -legacy -out test.pfx -inkey test.key -in test.crt -certfile test.crt
    // Password: PfxPassword
    // Thumbprint: AC6C1CDD9050FC943A4A67DAA181C85CF89AE9C7
    private static X509Certificate2 TestCertificateWithPrivateKey = new X509Certificate2(Convert.FromBase64String(@"
MIIPywIBAzCCD4cGCSqGSIb3DQEHAaCCD3gEgg90MIIPcDCCCbkGCSqGSIb3DQEH
AaCCCaoEggmmMIIJojCCCZ4GCyqGSIb3DQEMCgECoIIJdjCCCXIwJAYKKoZIhvcN
AQwBAzAWBBCVPX0e9RbyA5c11lIfilqwAgIH0ASCCUjpZNP0mNeFdtLLOR1von4z
CkhwozAkc6Wd2CTsOwnBR+FwcHvuEioKT/0bM2f/uxPl25ohvCi1DAAc3DOH2uYJ
aAaTcEzkIf4OoIGE2eYhkMDL0alv3OrrsMvAFJZwXpxiWKWskswsZSjted9GX4eV
XGRRVyJ/jVIS5fyMDORYf3l8xT+X0u6ridXsuxvl6ZAm9w16FC4kbnMmzhP6+Yk3
SzoT292w8eBtxb16OzzhS1Q+2AJ9/FN2Fcx1xxv0MT9zCJTMu/7b3Bj6efZtl2Oo
hRmxKbsqes29Ow9HZsgObGipKwq5JRtI98Q0SmIRxd+iqaf/X6mME1W+g/HjWAIF
5iD25n4zpngH4Zm+a4Q3HZ7kRu1R3BRS6mkvHI8dL0enFJVm/sVO3U0gYkVt+P8A
WSiLxWe0VbZ07Ihh+GwK8frrftZfNFL8cQfafo90cijZe4esp1S71GYuQMyl35Mg
0BezlqwlvosN1t7OBEJehmTPfh5LzEqaA1Zb67GqGNABBqG9qd9OMVgRdR43tYXi
awOv28nazd7uLfx1RY0zHDyisucG+y/fB8Zx+Yw1DCM0lFdfy9jCX7rgLykccNNA
JFUnPN+MiWTvxJ5qlaK10CCK6LGOgK0sSLTnN2Y81R/0Ne5bcXIn069hCP9HEHrU
5uPDGG1P3ALL82oL6ik/VkWV6qb9NBEBwFGkmkdnchE4nSY9IIm6AMZTxKWs8q7x
pVOsDQszV025BzXGhqmdn3h9x96j4UR/kpsxN3Ni5Dup4vmmhe08q/m1xJLlDNnu
Wzi4ELV9UPQHaj/jR26dckasgTaHrir3FK5e1HuJMxvxrgXhaVmqgA1Bchnid7eU
fbuySYDFf+Jy7YXyAOZtz5mKSgDyRXWzIcGdR5zAzMVaHJ2FrfE61+U51V0Jllt5
0e4+qL4QoB3JLI/wGiyAvy/u+gNyAulWMwbeyeS3055DT53mu2FRnTRHfX4vUf8B
lyDAYJxPj49HFHv1xeegRU9gwZPG1c/iVbZ4FyBj1vlQGFfFEGOc0J166iDqlwjp
c3LPzOxysXKLxMeU8oL2AxAsnJN4FDBIzOLYPYl6uB+HugGYr2zQGg3iTNCXf5SM
MtfGh2Uzl9SGsa/mU6usLXXXw5j9PuwrqURQtt+r0yYKoYoIp4UpSI27e0y990oP
7mOy1H9KJpAstWoMH+OTIQTkBvAotZL5OXdXdhDSIvm+BgAL5w2agW5IctYbAbi8
uAeCYcqgESmSDnBu/sPuzmWsWo8fu45uBAAkflmFaF6PWqzAL3MddLHpgcZk4u+c
5DlqrGrRQSGLBzB0naOr4KkKoGFlBdSwJRBAUiXkHUVIW/swMaW67IgOWx0nq9GQ
aXoMMPnQWgX4DIobzGnqCx8Df+ch7M4pwzJqIh4gc3lOyf1eT42Y7WMrd44AOgW/
/pVuY3SqIlSoZjZJmrdsRsrFpyBIp4ghr6KjisZrEDFHSnAmk+6cGwDbA9fuFSKO
hYKcjM/nOpyWvKnCTCRWk1TbCghF6tb2m45sOJZzz1UFO+38GsAwSgL3JRQtxTOq
444UmNJvIeZaY1CC39ci5U7z+RgIZk74UkBqQOn3LZ9uCRUhp6GHHfJqu8S1Dp22
EIYsY7Y7sa6MHDbX10X3EFJMYhvloZg6/7TbtGoa+muLe+mVAcCpXcuzeoPkULGw
/QY0YPWvdDBf0xNNMhsCn4cz5rDBlhScXCiC7UoPtlrF2ZPpiONGohFTT0CknhYb
UsA+52W3cMYrnoanZn6zPHrxL0wAf2RfG9C/+vxJ/MVxMgJwnkHSrSHpRLH2/YDe
gOAvNR2qawdaEhpGFVMbiWTiYz+eo9JZ2yVukGE/g7v/YJTs0I3fKYfslWMzJN/h
5emrxwvR8+fSeBIOHygWhlRudRprj3QryGvLOUeDuCVNu3lQMg8fpyKJeEHnJNQJ
w2IOF6UORiO7FD64LxCRxpjYjsthjMiQu84Ny6pKbHqE2DqxZHOTnDoDzKDVN7Fi
EaLjPUCXMthyZ3iH0UVC013dEhBQ/Qty5tngosDuTVgX44lTL5Cnzbx0x5TCMsJ+
CP2fVqTDmvXpPaURdNK4JMGzMRJcDjhDnL8FCI9iSutGU6dbhIueM+44/XB2f2YC
wJ8ccH4StrOf7rmaPXlhTNB+GT9l4SkuEBEcMTBENUd7dZxnrrByiD/zMu7FnsOL
hASrucXpJ9Y+zEhg3voFojhHIGhKQ+9jNAlmTJVrSmHfWBT7DssWkXtULhZXTiKn
ctwTOyIxDOZfqd9FZ1eI2panoppOWgDaTRqu0wD6fptiEfo/JzXlXiMqmSRDoO+W
TH/EIdNGdlyVCNiprcjX8IdgDcsadGixTXl7mBjD3jNi8DudA+oGB+Kaxbsy9QMu
l+YetofUArE1dTSsILpc05ytAZ4wgj2w/yHnDtunQQ0TYsrI1edGMsMTKSTgfX34
sgAthaX1V1m9NOrvRS4WpIBKRYNprIOL7aqalTAlZmtPj31p24+Q2VPHdzUd0S4g
z+mlMZ9KShobHYdqawlPJVIjxl+TpKWrHsZvp8KHtW33irPL4r5jKkylBjrGf7+x
OAWVRVOqtWeCZH1vekrC0ssVzucQJCJc3gz3pDgLPWSxVTYgQRLhSqlky02ksPya
+//D9bY7U6GIWO8ohKwgzpdUV27j9DGRB9LV/Qchpn9R5Ds+QF/bHidT1uzZtW7l
OSXlRW7pPIO2IqHy4QvmsjyOzL8L88hZMkO0bymZTe//haLEUiHCZLrxqMzK6xC4
d65ZyDAxALbcMNxBcoys+4Udlyjk5Uk/sbyD1QFinz5lxzHGSWAixBlHPgj/OBRr
QcyrWxW5AUQ2h3RqB+oPvuqcP0SpG2Q7trG2XuJVWzcFM9OcAH70IVm+o4MUMNm9
uwPpKVaTmX55awBqc47BIvrY8mghU+zIsK7JiDTAgvUiDuZOnvietzI37jiy5kjG
x9oBxL4BM8IW+s74d44bfzsqRHCE9ECUq1bGre56/MennrMOXhXjqv4fnYxFPx6C
Tkk2Wct43o2RIMUqK6EC5Txha/N258OaWztZTwm/A/Y6iG8B+YSuQ6DNgLII9t//
9XzcI7R7MPEJ4xO+eTWhdORY9jKcF1Rl2bKZE8O7xQYAG2oFG/ai27EyxrZLcn3H
cksVx/I7W4kxFTATBgkqhkiG9w0BCRUxBgQEAAAAADCCBa8GCSqGSIb3DQEHBqCC
BaAwggWcAgEAMIIFlQYJKoZIhvcNAQcBMCQGCiqGSIb3DQEMAQMwFgQQW9GH8ykz
nqoGw48cJBOdvAICB9CAggVgvg+PTPpFXYUYPyEfau3ufR3E8JXNYWzcasXUyyUI
I2UCTX6Is3SD5Xly2tzn0fxTURdxhsj4miGzRnkI2wfj6P27tYn1LRaJUHozLmsl
pgklasRxeZFRdHgqyJ6xS81luU+3OWiFbYMNXCS9wTkcroY922CRnSXsV9zs9+C8
sXCMlhlS3gCk/qlcTMhIFj0vVFuaGabgNhR7Jn76U7OQdujczgXgfLHgItVl08oA
PVVqwS3DjJeuIaYDpvOaQzMcjWQtQM9f5maaaR+Wo9h5hGvvoLHnLutzU3eajxK0
2VygDgM7llVh+BtyTziuyFg/8UBprx+9C7IWJyje+x/7eTiBHyD0tQSP4a8m/awK
Mu90kdvvVVOgfEJhobkQWtTkWOnSPj7xOJWcBT7IHn73WYJs8p+1MHFgcJLT30ow
yS4xzTWXt63AlPGbAcqlVXYORJLcZ/eZnj3oJk2p/hBkj09ghu4H43GsSNThsnR+
sOl8DKluJIFul+tNfiUKHSMtDrmTxq2wHOMaX+4qcU1uUpnixye6HnlIfJWbo8E5
o1voQ23xvHDRFmt/Mq5gHinVwSwa2V3NXkp7Dzl8XTnbwhp1mHtH8ssucnOjsC1g
N+HybMd6kMcO8U+qDWqqTac6tA6KK3HqGDuod6+Fdi9EiY5sBgZ/ana8XViscDh5
Rn9k8qOo22/DXfGraF3FAvPibijLKkqa76VUiUYOgPpGQD0EOIz6fHDlqB+zIvNN
4CHOZAKiOsUmUMJdFLkzLQYaSLb6bKTH7echuoDvXnAMNEuCURcKo/YYQd3Se1gC
j7ulsyo+k3WBMa2LZ6gRJWiD5raDPRVMt+tosvDV/Bbl7xw2Sifkl8g4JJFfhHA8
tuf19to0sr5SE3nSGMIK4VKyxnhtvFUUN7gRXyJNsupbseGU/zakN9foYOPhx4Op
ve58sEFPWQD4+Tki1zS8KIqulu7fjdZUOBFHX3aF8c36iG77jz5uByxvwPU2v0O1
UVMU2/wpt8j+ZyEcpS0bdTL7PrhBxJoQAhlFiyxN0t/kCbGUSXZlUfqFHfQLf0Hl
gAbNUJONfRMykYRnE3a6viMMoF86bXEoxBjrNS0BXCt1Z4SWGeZzmnuUT2vBPx91
l9SZAu+DB+4qP8r9ht1qTWeC9sd+VKrUuW87IPetEsk5BZtw1u+9HrWsGtnHs34s
9i2JFfrdDlPbIcFMLGAgS1Bs1YEe5uxJaAzZtNuSHyN55JODUiwzbiPfjOakmQeV
mUvZwfwP3MANRm+3y8jPoKaDKhoY70lENsR5dyxbkJk0WnSl6XH3nOE9I6M86kqQ
NfuaR21RDHPUHZUxpkhfp1bySW0lA//E0LGTZUjOHw7VXZ3kA0mwzLQ9ySRb9a5h
mTx6qLAI2y9uoxuU6/DODuBPRgz+cb3qHHXDoI3RV8GEDTIZx3Bkbtxv6eK92FQW
3yLeA0uVFwqo79kX00hJuXFWg3r6xwgpJdLJr+Uymhf2g7PmHr9Zip5yWKqb7fP+
jEvRKE5KD1QFXd/xPxif42VzazF6yX5GF1WrvN1EeUVQZkeXQX+Js3EqoOS/VdJ0
CI7q5nPGypEnp+1twjqGz5a9qBEqSCjr1RNMRVcDFJC9UxdpPPZT5xFKLhIOe3YD
tunu0saI07dsD05FWrSVvw9bGULbUpd3Ah8Q3WWRasp3mprK38xoUztkxfQ8nWUZ
BeKEIO7DvgrBUw2PBK+s22suRjHwrVOZDJg9/OIeRauusCUqaCoxbIlwdedlhOBO
fhFjJWnvEfHCzN9p8d/bPWw0GmfCkul8mkL8RmL35ZhD487pkpQkl/lKay1V8LFR
YpMwOzAfMAcGBSsOAwIaBBQY9vd9EdUgCQYA+xTPnUa+5tOsrAQUAJL6m/he/l7+
aGur7E/GqOtPjjECAgfQ"), "PfxPassword");

    private static X509Certificate2 TestCertificateCerFormat = new X509Certificate2(Convert.FromBase64String(@"
MIIFDTCCAvWgAwIBAgIUMOrXEcRBfYU9Jr6coUXIpSbdLkEwDQYJKoZIhvcNAQEL
BQAwFjEUMBIGA1UEAwwLTGljZW5zZVRlc3QwHhcNMjQxMDI3MTYyNzM4WhcNMzQx
MDI1MTYyNzM4WjAWMRQwEgYDVQQDDAtMaWNlbnNlVGVzdDCCAiIwDQYJKoZIhvcN
AQEBBQADggIPADCCAgoCggIBAIiQRt6N+M/tUri0f/9gVr1RnW9KNy4NvIhOtGrw
Z3gbai/gRHdmaL8mNunTqPBYQYm+JOVklmHqJSyI6/cUuy+WgrUU9ewrVZAQUoK7
DdirP1X0igtOa2gnyQxL7TXzmtQiNSFEbk8SknEteCaMqixpZ3vZHGVSEw0IuFQT
pPSDobmmZxRnOrsfS2TWw1hdiYTrpRCUnjsQ67XjFQmVG1OMuTc4eP611cK+5OfS
g0CFXqjHrQEzbguSbAidlOilkvKwDI1Cb5WOae97JiZEhZoARMuE+XvQdHcFk/u9
CDpDfO9zK4PeDXFE0u45dsyKEIVFqZ2Ts21Cfa4HwKmtxr//L4jJ4Q2zcOZAqHhO
PbdshhvWKLeypzT22M/02tfy/2uLUfG9SOoejVN0y3vRZEkheTLUypC0AGx5m48D
tnzR2lFZzelIXqIBXJUVBAW45t+KUhHiekQ8GAlK9NDW/9WTNwwPUbhoJyI/Fid9
B8VJCHC9Qof80+ymqmjoRPZMQTE6/e2A2U3ESUFhbJLKU50e/CQoDOdD+x83zz3U
ayYAjZMdkYEKV/Okj6wKWptnUhzgArnOJxCiuTgbcddoKKSaOzbKA222eR66rIdv
bSAJHyMIwzmwVqyKaDTZdKBsX15vmbQp8pZmSjP0CHk8bQM++lyK8wLTYj321cHl
M2x7AgMBAAGjUzBRMB0GA1UdDgQWBBT9Xt+mJZlanInw5CmJArjMXFbTEDAfBgNV
HSMEGDAWgBT9Xt+mJZlanInw5CmJArjMXFbTEDAPBgNVHRMBAf8EBTADAQH/MA0G
CSqGSIb3DQEBCwUAA4ICAQB2mPFm7hr37eyL9y8OV7hq1sB0n2CjEMoTI36p0NpV
V2rlTnboVifDXtdx/EhMAeiHKcks4Ccn/DvP6sthYsRAGv20+TCjuIkjvt/I4u7C
0udT65oIDUSwcguZ1KYISqE2xBmziN1hJgNKG9hPPeN+mRVX0JxQq3lH4nQSwZD9
mxX0Ep6eZlKsEvoViYel24KO0WRF+nSx5u7xhi1sA6sS9hQK1NgdQYNqweOFmYuA
G+/PcVXJbVXdleXfsCiQidXHqmlPaNgKVoH/udsBMe+y6V70towJRZsjSAAZjK87
L4UZFENROmcUMOQE0rCO0KYUBFQPg1UkZFUoOHC3o0Mt0SNC1uJF50MpJDc78Txf
hVraMXRziy9ZSeiqvfffNVv3rVVCs6MrJObmVsm4ZkGVQX7Z7AM7IFVNghy/YOUu
Q9srnDdBXunRt6PYroWD3u4kWFkrJeQMU/TthEm/BfxEkShk59bVVworxYs8d7zL
bp07BTFJ0xhFwv3+jLvwT2WRnmPmWZSySbyeWrU1Pf//4vRlVtnNenxqEUrQW0P7
p3FMP7lYoJAc0J1uTcJzJT6ncr4+YAkpvMiINYI75fPCVKnGKn71bC70oryPn5ZD
SD9PbavjyDl00o5G6XWPy5x7XfjjLwDN5Lah1t1fCIAFDEleCj1Ml0FaapygrlGw
oA=="));

    [Fact]
    public async Task RoundTrip_Works()
    {
        var cloudSut = CreateSut(options =>
        {
            options.SigningCertificate = TestCertificateWithPrivateKey;
        });

        var license = cloudSut.CreateLicense(
        [
            new Claim("myClaim", "hello world!"),
        ], TimeSpan.FromMinutes(5));

        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = TestCertificateCerFormat;
        });

        var claims = await selfHostSut.VerifyLicenseAsync(license);

        Assert.NotEmpty(claims);
        Assert.Contains(claims, c => c.Type == "myClaim" && c.Value == "hello world!");
    }

    [Fact]
    public void CreateLicense_WithSelfHost_Fails()
    {
        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = TestCertificateCerFormat;
        });

        var invalidOperation = Assert.Throws<InvalidOperationException>(
            () => selfHostSut.CreateLicense(Enumerable.Empty<Claim>(), TimeSpan.FromMinutes(5))
        );

        Assert.Equal(
            "Self-hosted services can not create a license, please check 'IsCloud' before calling this method.",
            invalidOperation.Message
        );
    }

    [Fact]
    public async Task RoundTrip_Expired_Fails()
    {

        var cloudSut = CreateSut(options =>
        {
            options.SigningCertificate = TestCertificateWithPrivateKey;
        });

        var license = cloudSut.CreateLicense(Enumerable.Empty<Claim>(), TimeSpan.FromMilliseconds(10));

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = TestCertificateCerFormat;
        });

        var validationException = Assert.ThrowsAsync<Exception>(
            async () => await selfHostSut.VerifyLicenseAsync(license)
        );
    }

    // TODO: Test license signed with a different key

    // TODO: Test verifying license with a different key

    private DefaultLicensingService CreateSut(Action<LicensingOptions> configureOptions)
    {
        var options = new LicensingOptions();
        configureOptions(options);
        return new DefaultLicensingService(Options.Create(options), _fakeTimeProvider);
    }
}
