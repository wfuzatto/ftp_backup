# FTP Backup

Aplicativo Windows para compactar uma pasta em ZIP e enviar o arquivo para FTP/FTPS de forma agendada.

## Arquitetura

- FtpBackup.Service: Windows Service. Executa agendamento, ZIP, upload, retry, retenção e recuperação após falha.
- FtpBackup.Tray: interface WinForms com ícone na bandeja, configuração, status, teste de FTP e botão Backup agora.
- FtpBackup.Core: regras compartilhadas, proteção de senha, scheduler, ZIP e FTP.

O serviço e a interface são separados porque o Windows isola serviços da sessão gráfica do usuário.

## Recursos da V1

- Origem e staging configuráveis.
- FTP, FTPS explícito, FTPS implícito ou modo automático.
- Host/URL, porta, usuário, senha e pasta remota.
- Senha protegida com DPAPI do Windows (LocalMachine), nunca salva em texto puro.
- Agendamento: a cada hora, diário, semanal ou mensal.
- Horário/minuto de início, dia da semana e dia do mês.
- ZIP com suporte a arquivos grandes.
- Arquivo remoto temporário .uploading e renomeação somente após upload concluído.
- Verificação do upload por tamanho/checksum quando suportado pelo FTP.
- Arquivo SHA-256 opcional ao lado do ZIP.
- Retry configurável.
- Retenção local e remota.
- Logs em C:\ProgramData\FtpBackup\logs.
- Estado persistente em C:\ProgramData\FtpBackup\state.json.
- Marcador de execução para recuperar backup interrompido por crash/reboot.
- Windows Service com inicialização automática e recovery/restart.
- Ícone na bandeja; fechar a janela apenas minimiza.
- Botão Backup agora.
- Botão Testar FTP.

## Observação importante sobre arquivos em uso

O compactador abre arquivos com compartilhamento de leitura/escrita quando possível. Mesmo assim, copiar diretamente arquivos ativos de bancos de dados (MySQL, PostgreSQL, SQL Server etc.) não garante consistência transacional. Para bancos, prefira gerar dump/snapshot/VSS e apontar o FTP Backup para a pasta resultante.

## Instalação

Abra PowerShell como Administrador na raiz do repositório e execute:

    Set-ExecutionPolicy -Scope Process Bypass
    .\scripts\install.ps1

Para desinstalar:

    .\scripts\uninstall.ps1

Para remover também configurações, logs e staging:

    .\scripts\uninstall.ps1 -PurgeData

## Desenvolvimento

    dotnet restore .\ftp_backup.sln
    dotnet build .\ftp_backup.sln -c Release

## Segurança

A senha FTP é cifrada pelo DPAPI da máquina. FTPS é recomendado sempre que o servidor suportar TLS; FTP puro envia credenciais e dados sem criptografia.
