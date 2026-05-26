# TODO

## Servidor

- [ ] Corrigir o logging do servidor para incluir a identidade do utilizador (email/role) em cada request - Dinis
- [ ] Investigar que valor usar para `maxFrameSize` na configuração das WebSockets

## Daemon

- [ ] Analisar e melhorar o error handling do Daemon
- [ ] Investigar como tornar os monitores mais robustos
- [ ] Investigar a performance do daemon como serviço do Windows principalmente ao parar
- [ ] Corrigir o problema em que o serviço não interceta nem termina processos proibidos
- [ ] Adicionar verificação de que o daemon está ligado (CONNECTED) antes de considerar o estudante pronto na sessão - Dinis
- [ ] Refactor da implementação do conteúdo do payload
- [ ] Distinguir e classificar cada evento lançado por um monitor
- [ ] Guardar os eventos lançado durante a perda de rede da parte do aluno

## Consola do Professor - Miguel

- [ ] Adicionar filtro/arquivo para ocultar exames e sessões terminadas
- [ ] Adicionar opção de eliminar ou arquivar exames
- [ ] Integração do login com OAuth

## Documentação

- [ ] Reestruturar a secção Introdução e começar com a secção do Enquadramento ou Requisitos Funcionais **(3º prioridade)** - Miguel
- [ ] Refazer os diagramas com menos ligações e melhor layout **(3º prioridade)** - Miguel
- [ ] Analisar o código do repositório

## Dúvidas


## Concluído

- [X] Integrar o daemon com WebSockets [12/5]
- [X] Resolver vulnerabilidades do ClipboardBlocker [16/5]
- [X] Evitar o logging repetido e excessivo especialmente no NetworkMonitor e ProcessMonitor [17/5]
- [X] Desenvolver o esqueleto da consola do professor
- [X] Organizar estrutura da implementação do ClipboardBlocker a nível da plataforma

