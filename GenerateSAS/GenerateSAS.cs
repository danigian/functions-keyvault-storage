using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Azure.KeyVault;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.KeyVault.Models;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Renci.SshNet;
using System.IO;
using System.Text;
using System.Linq;

namespace GenerateSAS
{
    public static class GenerateSAS
    {
        static SecretBundle secret;
        static SecretBundle sshPrivateKey;

        static string _vaultBaseUrl = Environment.GetEnvironmentVariable("KEYVAULT_BASEURL");
        static string _storageAccountName = Environment.GetEnvironmentVariable("KEYVAULT_STORAGE_NAME");
        static string host = Environment.GetEnvironmentVariable("KUBERNETES_DNS");
        static string sshUsername = Environment.GetEnvironmentVariable("KUBERNETES_SSH_USERNAME");
        static string sshPubKeyFingerprint = Environment.GetEnvironmentVariable("KUBERNETES_SSH_FINGERPRINT");
        static string kubernetesSecretName = Environment.GetEnvironmentVariable("KUBERNETES_SECRET_NAME");

        static string base64sas;

        [FunctionName("GenerateSAS")]
        public static void Run([TimerTrigger("0 */30 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
            KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            
            //signedPermissions: Allowed values: (a)dd(c)reate(d)elete(l)ist(p)rocess(r)ead(u)pdate(w)rite
            //signedServices: Allowed values: (b)lob(f)ile(q)ueue(t)able
            //signedResourceTypes: Allowed values: (s)ervice(c)ontainer(o)bject
            string _sasName = "blobrwu4hours";
            Dictionary<string, string> _sasProperties = new Dictionary<string, string>() {
                {"sasType", "account"},
                {"signedProtocols", "https"},
                {"signedServices", "b"},
                {"signedResourceTypes", "sco"},
                {"signedPermissions", "rwu"},
                {"signedVersion", "2017-11-09"},
                {"validityPeriod", "PT4H"}
            };
            SasDefinitionAttributes _sasDefinitionAttributes = new SasDefinitionAttributes(enabled: true);

            try
            {
                //Sas definition create/update should be in a different function
                var setSas = Task.Run(
                () => kv.SetSasDefinitionAsync(_vaultBaseUrl, _storageAccountName, _sasName, _sasProperties, _sasDefinitionAttributes))
                .ConfigureAwait(false).GetAwaiter().GetResult();
                log.Info("Sas definition created!");

                secret = Task.Run(
                    () => kv.GetSecretAsync(_vaultBaseUrl, $"{_storageAccountName}-{_sasName}"))
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                base64sas = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret.Value));

                log.Info($"Here there is the base 64 encoded secret: {secret.Value}");

                sshPrivateKey = Task.Run(
                    () => kv.GetSecretAsync(_vaultBaseUrl, $"privatekey"))
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                log.Info($"Retrieved {sshPrivateKey.Id}");
            }
            catch (Exception ex)
            {
                log.Info($"Something went wrong with KeyVault: {ex.Message}");
            }

            try
            {
                //By choice we do not have passphrase for the key stored in Key Vault
                PrivateKeyFile privKey = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(sshPrivateKey.Value)));
                
                using (var client = new SshClient(host, 22, sshUsername, privKey))
                {
                    byte[] expectedFingerPrint = StringToByteArray(sshPubKeyFingerprint);
                    client.HostKeyReceived += (sender, e) =>
                    {
                        if (expectedFingerPrint.Length == e.FingerPrint.Length)
                        {
                            for (var i = 0; i < expectedFingerPrint.Length; i++)
                            {
                                if (expectedFingerPrint[i] != e.FingerPrint[i])
                                {
                                    e.CanTrust = false;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            e.CanTrust = false;
                        }
                    };
                    client.Connect();
                    var delete = client.CreateCommand($"kubectl delete secret {kubernetesSecretName}").Execute();
                    log.Info(delete);
                    var create = client.CreateCommand($"kubectl create secret generic {kubernetesSecretName} --from-literal=secretKey={base64sas}").Execute();
                    log.Info(create);
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                log.Error($"Something went wrong with Kubernetes: {ex.Message}");
            }
        }
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }
}
