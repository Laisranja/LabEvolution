# Neuroevolução

Uma rede neural 3-4-2 (26 pesos) é evoluída por algoritmo genético, sem backpropagation. Nesta versão foram adicionados:
rota de referência (A*), métricas de eficiência, validação em 3 cenários novos e save/load do melhor genoma.

## Arquivos (`Assets/Scripts`)

- `NeuralNetwork.cs`, `Genome.cs`: rede e genoma (da aula; `Genome` ganhou `FromWeights`, que copia pesos salvos).
- `EvolvingShip.cs`: nave com sensores, fitness e medição de tempo e distância real.
- `EvolutionManager.cs`: evolução, melhor genoma, validação, save/load e botões na tela.
- `ReferencePath.cs`: grid + A* (rota de referência).
- `EvolutionSnapshot.cs`: dados salvos no PlayerPrefs.

## Como rodar a cena

1. Abra `Assets/Neuroevolucao/Scenes/Neuroevolucao.unity`.
2. Aperte **Play**. A linha amarela é a rota de referência; as naves azuis evoluem.
3. Botões no canto da Game view: **Salvar melhor**, **Carregar melhor**, **Apagar save**, **Validar nos 3 cenários**.

A pista tem 30 x 20 com paredes e um obstáculo central. O Spawn fica à esquerda (-12, 0) e o Goal à direita (12, 0).
Layers: `Obstacle` (paredes e obstáculo) e `Ship` (naves).

## Rota de referência (A*)

Usei A* em grid em vez de NavMesh, porque o Goal e o obstáculo mudam de lugar nos cenários de validação e a rota precisa ser recalculada a cada um.
As células que encostam em `Obstacle` (com folga do tamanho da nave) ficam bloqueadas e o A* anda em 8 direções.
A rota aparece como linha amarela e os pontos vão para o Console. Ela **não controla** a nave, só serve de comparação.

No cenário de treino a rota tem **49 pontos e 26,42 m**.

Limitação: o caminho segue o centro das células (sem suavização), então pode ficar um pouco diferente do caminho ideal.

## Métricas por episódio

- **Chegou / colidiu:** `true` ou `false`.
- **Tempo:** segundos simulados até o fim do episódio.
- **Distância real:** soma do deslocamento da nave a cada `FixedUpdate`.
- **Distância de referência:** comprimento da rota A*.
- **Eficiência = distância de referência / distância real.** Só é calculada se a nave chegou e se as duas distâncias são maiores que zero; senão aparece "invalida".

A eficiência pode passar de 1 (ver cenário 1). Isso acontece porque a nave para ao tocar na área do Goal (um trigger de 1,5 m) e porque a rota do A* anda em degraus de célula, o que a deixa um pouco mais longa que o ideal.

## Fitness (igual à aula, não alterei)

`fitness = (distância inicial − distância atual ao Goal) × 10 + 100 (chegou) − 50 (colisão) − 1 (timeout)`

## Seed e parâmetros

- Seed de treino: **12345** (campo `Random Seed` do Inspector).
- População 30, elite 2, taxa de mutação 0,08, força de mutação 0,25, episódio de 15 s.

Evolução observada (melhor fitness da geração, linhas do Console): geração 0 = 175,13; gerações 1 e 2 = 175,64 (ninguém chegou ao Goal); geração 3 = **324,69** (chegou, sem colisão, 5,0 s, 31,1 m percorridos para 26,4 m de referência, eficiência 0,85). Nas gerações seguintes (até a 8) nenhum indivíduo superou esse fitness, então o melhor global continuou sendo o da geração 3.

## Validação fora do treino (Parte B)

Genoma usado: **seed 12345, geração 3, fitness 324,69**. Ele foi evoluído só no cenário de treino e depois executado, sem evoluir de novo, nos 3 cenários.

| Cenário | Chegou | Colidiu | Tempo (s) | Dist. real (m) | Dist. ref. (m) | Eficiência |
|---|---|---|---|---|---|---|
| 1 — Goal em z=+5 | sim | não | 4,1 | 25,6 | 26,2 | 1,03 |
| 2 — obstáculo deslocado (x=1, z=-1,5) | sim | não | 4,9 | 30,9 | 25,6 | 0,83 |
| 3 — Goal em z=-4 e obstáculo em (x=-2, z=2) | não | sim | 4,4 | 27,4 | 25,8 | inválida |

**Resultado: VALIDADO**, com 2 de 3 cenários com chegada e sem colisão (o mínimo exigido era 2).

## Save e restauração (Parte C)

- Chave única `neuroevolution.best.v1`, JSON criado com `JsonUtility`.
- Campos: versão, seed, geração, melhor fitness, pesos (`float[]`), população, taxa e força de mutação e data/hora.
- Os pesos são **copiados** (`Clone`) antes de salvar e `PlayerPrefs.Save()` é chamado.
- **Salvar melhor:** grava o melhor genoma de todas as gerações.
- **Carregar melhor:** lê o save, para a evolução e executa o genoma salvo, sem criar geração nova.
- **Apagar save:** chama `PlayerPrefs.DeleteKey` e mostra "Save apagado." na tela e no Console (usei antes da gravação, para garantir que o save testado fosse novo).
- Sem save: aparece "Nao existe save" e o programa continua normalmente.
- Save com versão diferente, JSON ilegível ou quantidade de pesos diferente de `NeuralNetwork.WeightCount`: é recusado com mensagem.

## Análise (Parte D)

**1. O melhor fitness correspondeu à rota mais eficiente?**
Não necessariamente. Nas gerações 0 a 2 o melhor fitness (175,13 na geração 0; 175,64 na geração 2) era de naves que **não chegaram ao Goal**. A da geração 0 percorreu **66,9 m** em 15 s (a da geração 1 percorreu 67,0 m), mais de 2,5 vezes a rota de referência (26,4 m), e a eficiência foi inválida. Mesmo assim, ela tinha o maior fitness porque terminou o tempo mais perto do Goal do que as outras (cerca de 6,4 m de distância). Já o genoma da geração 3 (fitness 324,69) chegou com eficiência **0,85** (tempo 5,0 s). O fitness só mede quão perto a nave terminou do Goal, mais um bônus fixo de chegada. Por isso duas rotas de comprimentos diferentes que chegam ao Goal (como 25,6 m e 30,9 m nos cenários 1 e 2) recebem fitness praticamente igual.

**2. Em qual cenário o agente teve pior desempenho? Por quê?**
No **cenário 3** (Goal em z=-4 e obstáculo em x=-2, z=2): a nave **colidiu aos 4,4 s** e não chegou. É o cenário que mais muda em relação ao treino, porque o Goal e o obstáculo foram movidos ao mesmo tempo. No último quadro da validação a nave aparece parada junto da parede leste, perto de z=0, o que sugere que ela seguiu em linha reta como no treino (onde o Goal estava em z=0), passou 4 m ao lado do Goal e bateu na parede. Isso indica que o genoma aprendeu o trajeto do cenário de treino em vez de um comportamento geral de ir ao Goal desviando de obstáculos. Os sensores só veem paredes, não o Goal, então a rede não tem como saber que o Goal mudou de lugar. (Esta explicação é uma interpretação do que se vê no vídeo, não uma medição.)

**3. Qual alteração no fitness poderia induzir um comportamento indesejado?**
Se eu penalizasse a distância percorrida para forçar rotas curtas (por exemplo, −1 por metro andado) sem aumentar a penalidade de colisão, o fitness criaria um atalho. Uma nave que vaga 67 m, como as das primeiras gerações, perderia 67 pontos, mais do que os −50 da colisão. Bater logo na parede nos primeiros metros passaria a valer mais do que tentar chegar, e a população aprenderia a se suicidar. A mesma ideia vale para o termo de progresso: ele já gera um ótimo local (as naves da geração 0 a 2 ficaram em 175 sem chegar), e aumentar o `progressWeight` reforçaria esse comportamento de ficar perto do Goal sem chegar.

**4. O genoma salvo reproduziu o comportamento esperado após reiniciar a cena?**
Sim. Salvei na geração 3 (**seed 12345, geração 3, fitness 324,69**). Depois parei o Play e iniciei de novo, o que recarrega a cena do zero (geração 0, melhor fitness 0,0). Ao clicar em **Carregar melhor**, o painel mostrou `Carregado: seed 12345, geracao 3, fitness 324.69` e a nave executou o genoma sem criar geração nova. **Resultado: chegou = sim, colidiu = não, tempo = 5,0 s, eficiência = 0,85.**
