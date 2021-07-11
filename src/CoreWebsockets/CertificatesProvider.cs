using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace CoreWebsockets
{
    public class CertificatesProvider
    {
        public static X509Certificate2Collection GetClientCertificates()
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);

            store.Open(OpenFlags.ReadOnly);

            return store.Certificates;
        }

        public static X509Certificate2 GenerateCertificate()
        {
            using var parent = RSA.Create(4096);
            using var rsa = RSA.Create(2048);

            var parentReq = new CertificateRequest(
                "CN=localhost",
                parent,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            parentReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));

            parentReq.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(parentReq.PublicKey, false));

            using X509Certificate2 parentCert = parentReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-45),
                DateTimeOffset.UtcNow.AddDays(365));

            var req = new CertificateRequest(
                "CN=localhost",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DataEncipherment,
                    false));

            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                    new Oid("1.3.6.1.5.5.7.3.8")
                    },
                    true));

            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var cert = req.Create(
                parentCert,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(90),
                new byte[] { 1, 2, 3, 4 });

            return cert.CopyWithPrivateKey(rsa);
        }
    }
}
