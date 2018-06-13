# functions-keyvault-storage

Please refer to [this blog post](https://www.danielemaggio.eu/containers/storage-keyvault-functions/) in order to understand what this repo is for.

Note that in order to get this code working you will need to configure some environment variables by issueing:

az functionapp config appsettings set --name damaggiofuncstorkv --resource-group StorageKeyVaultRG --settings KEYVAULT_BASEURL="https://<keyvaultname>.vault.azure.net/" KEYVAULT_STORAGE_NAME="damaggiostorage" KUBERNETES_DNS="<something>.northeurope.cloudapp.azure.com" KUBERNETES_SSH_USERNAME="azureuser" KUBERNETES_SSH_FINGERPRINT="3ec2167eb65d55fd9a707425ef0ce5ax" KUBERNETES_SECRET_NAME="thesecretname"
