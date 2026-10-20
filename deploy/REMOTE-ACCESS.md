# Servidor sempre ligado e acesso fora de casa

Estes passos são opcionais. Sem eles, o servidor corre só neste PC, em `http://localhost:5256`, e arranca com o `Iniciar servidor.bat`.

## 1. Servidor como serviço do Windows (arranca sozinho)

Na pasta do Release (a que tem `Server` e `App`), com uma PowerShell **como administrador**:

```powershell
.\install-service.ps1
```

- O serviço chama-se `TraderServer`, arranca com o Windows e reinicia sozinho se falhar.
- A base de dados e as chaves continuam em `Server\` (`trader.db`, `appsettings.Production.json`).
- Para remover: `.\uninstall-service.ps1` (não apaga dados).
- Para atualizar o servidor: pára o serviço (`Stop-Service TraderServer`), substitui os ficheiros de `Server\`, e arranca-o (`Start-Service TraderServer`).

## 2. Backups

O servidor guarda uma cópia da base de dados por dia (as últimas 14) em `Server\backups\`. Podes ver e criar backups em **Configurações → Sistema**.
Para restaurar: pára o servidor, substitui `trader.db` por uma das cópias e volta a arrancar.
Convém copiar a pasta `backups` de vez em quando para outro disco.

## 3. Usar a app no telemóvel fora de casa (recomendado: Tailscale)

O Tailscale cria uma rede privada e cifrada entre o PC e o telemóvel. Não abres portas no router e o servidor nunca fica exposto à Internet.

1. Instala o Tailscale no PC e no telemóvel (tailscale.com) e entra com a mesma conta.
2. No PC, ativa os certificados HTTPS: no painel do Tailscale (admin → DNS) liga **MagicDNS** e **HTTPS Certificates**.
3. Gera o certificado (PowerShell, na pasta do Release; o nome é o do PC na tua rede Tailscale, por exemplo `meu-pc.tail1234.ts.net`):

   ```powershell
   .\tailscale-https.ps1 -Hostname meu-pc.tail1234.ts.net -PfxPassword "uma-palavra-passe"
   ```

4. Cria/edita `Server\appsettings.Production.json` e acrescenta (mantém as chaves que já tens):

   ```json
   {
     "Urls": "http://localhost:5256",
     "Kestrel": {
       "Endpoints": {
         "Local": { "Url": "http://localhost:5256" },
         "Https": {
           "Url": "https://0.0.0.0:5257",
           "Certificate": { "Path": "C:\\...\\Server\\certs\\meu-pc.tail1234.ts.net.pfx", "Password": "uma-palavra-passe" }
         }
       }
     }
   }
   ```

5. Reinstala o serviço com `.\install-service.ps1 -AllowNetwork` (abre a porta só em redes privadas) ou reinicia-o.
6. Na app do telemóvel, em Configurações, define o servidor como `https://meu-pc.tail1234.ts.net:5257`.

Os certificados do Tailscale renovam-se a cada ~90 dias: volta a correr o passo 3 e reinicia o serviço.

## Segurança

- O registo de novas contas está fechado depois da primeira. Quem não tiver conta não entra.
- Há bloqueio da conta depois de 5 tentativas falhadas.
- Não ponhas o servidor diretamente na Internet (port forwarding) sem HTTPS válido e sem autenticação em dois passos, que a app ainda não tem.
