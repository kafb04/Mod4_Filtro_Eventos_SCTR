# Módulo 4 — Filtro de Eventos (WinForms)

Essa documentação descreve a **estrutura do código**, os **arquivos**  do módulo 4 e como se relacionam.  

---

## Visão Geral
O Módulo 4 é um aplicativo **Windows Forms (.NET)** que:
1. Recebe medições via **UDP** na porta **5002**.  
2. Converte as amostras em objetos `Measurement`.  
3. Avalia regras configuradas para os parâmetros **IA**, **IB** e **IC**.  
4. Gera eventos quando uma condição é satisfeita.  
5. Envia cada evento por **UDP unicast** para um destino definido no código.  
6. Exibe contadores por IED e o último JSON transmitido.

---

## Estrutura de Arquivos

```
/src
├─ Program.cs            # Bootstrap do WinForms (entrypoint)
├─ Form1.cs              # Núcleo: RX/PROC/TX, motor de regras e integração UI
├─ Form1.Designer.cs     # Layout da interface (controles, timers, etc.)
└─ JsonMod3.cs           # Classe auxiliar para montar o payload JSON de saída
```

**Relações:**
- `Program.cs` instancia e executa `Form1`.  
- `Form1.cs` utiliza os controles definidos em `Form1.Designer.cs`.  
- `Form1.cs` usa `JsonMod3` para gerar o payload dos eventos em string e bytes.  

---

## Variáveis de Configuração

No topo de `Form1.cs`:

```csharp
private const string mod3_ip   = "192.168.56.1";
private const int    mod3_port = 6001;
private static readonly IPEndPoint _mod3endpoint =
    new IPEndPoint(IPAddress.Parse(mod3_ip), mod3_port);
```

- `mod3_ip` e `mod3_port` definem o destino dos eventos enviados.  
- O envio é feito por **unicast** para `_mod3endpoint`.  

---

## Principais Tipos

```csharp
public enum Operator { Less, Equal, Greater }

public record Measurement(DateTime Timestamp, string IedId, double IA, double IB, double IC);

public class Rule
{
    public int Id;
    public string Parameter;   // "IA" | "IB" | "IC"
    public Operator Operator;  // < | = | >
    public double Value;
    public bool Active;
    public bool Verify(Measurement m); // compara o valor medido com a regra
}
```

---

## Fluxo de Dados

```
[UDP 5002] → RunRxAsync → _rxchannel ─┐
                                      ├→ RunFsmHybridAsync → EvaluateRulesWithTransitions
                                      │                              │
                                      │                              ├→ atualização dos contadores
                                      │                              └→ TrySendEventToMod3
                                      │                                         │
                                      └──────────────────────────────────────────┴→ _txchannel → RunTxAsync → [UDP → mod3_ip:mod3_port]
```

---

## Threads e Canais

- **RunRxAsync**  
  - Escuta pacotes em `0.0.0.0:5002` usando `UdpClient`.  
  - Recria automaticamente o socket em caso de inatividade.  
  - Escreve as mensagens recebidas no canal `_rxchannel`.

- **RunFsmHybridAsync**  
  - Lê mensagens do `_rxchannel` a cada ~50 ms.  
  - Faz parsing de JSON (campos `idDispositivo`, `id`, `IedId`, `IA/IB/IC`) ou CSV (`IED,IA,IB,IC`).  
  - Converte os dados em `Measurement` e aplica o motor de regras.  
  - Chama `flushuibatch(...)`, que mantém a interface responsiva sem exibir pacotes detalhados.

- **RunTxAsync**  
  - Lê bytes do `_txchannel` e envia via `UdpClient.SendAsync` para `_mod3endpoint`.  
  - Realiza até 3 tentativas em caso de falha.  
  - Registra o último JSON enviado na interface.

---

## Motor de Regras

- As regras são armazenadas em `List<Rule> _rules`, com acesso protegido por `lock`.  
- O estado anterior de cada regra é registrado para detectar transições.  
- Modos de disparo:
  - **Nível**: gera evento para cada amostra que satisfaz a regra.  
  - **Borda**: gera evento apenas quando ocorre mudança de estado (false→true ou true→false).  
- Contadores são atualizados por IED e em total acumulado.  
- A interface exibe essas informações de forma otimizada para não travar a UI.

---

## Envio de Evento

- A função `TrySendEventToMod3(rule, measurement, active)`:
  1. Monta o filtro em texto (ex.: `"IA > 100"`).  
  2. Usa `JsonMod3` para gerar string (UI) e bytes (envio).  
  3. Publica os bytes no `_txchannel`.  

---

## Interface

- A interface segue o layout definido no Designer.  
- O campo `txtLog` mostra o último JSON enviado, atualizado a cada 500 ms.  
- Os contadores por IED e o total são exibidos em uma `ListView`.  
- Não há grade detalhada de pacotes recebidos, priorizando atualização em tempo real.

---

## Rede

- **Recepção (RX):** escuta na porta UDP **5002**.  
- **Transmissão (TX):** envia pacotes para `_mod3endpoint` (por padrão `192.168.56.1:6001`).  
- O envio é sempre unicast, evitando broadcast na rede.  
