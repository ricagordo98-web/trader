using System.Globalization;
using Trader.Core.Models;

namespace Trader.Core.Help;

public sealed record TourStep(string Title, string Text, string Icon, string? Link);

public sealed record FaqItem(string Question, string Answer);

public sealed record ReadingLine(string Title, string Text);

public static class HelpContent
{
    public static readonly IReadOnlyList<TourStep> Tour =
    [
        new("Bem-vindo", "Esta app reúne notícias, sentimento e dados de mercado para te ajudar a decidir. Não executa ordens nem dá aconselhamento financeiro: as operações fazes tu na tua corretora.", "waving_hand", null),
        new("Notícias", "O feed junta várias fontes, com o sentimento de cada notícia e os ativos mencionados. Filtra por fonte, ativo ou tom.", "newspaper", "/"),
        new("Trading", "Cotações por tipo de ativo. Clica numa linha para abrir o gráfico completo e no balão para ver porque o ativo se mexeu.", "candlestick_chart", "/trading"),
        new("Gráficos", "Monta o teu painel: adiciona gráficos por tipo de ativo, arrasta-os, redimensiona-os e guarda a disposição.", "dashboard", "/charts"),
        new("Predição", "Sugestões de compra e venda com certeza, stop loss e take profit, mais o Radar de movimentos anormais, o track record e o backtest.", "insights", "/prediction"),
        new("Carteira", "Calcula quanto arriscar em cada operação e regista as tuas trades para veres o teu desempenho real.", "menu_book", "/journal"),
        new("Alertas", "A campainha mostra primeiro as tuas trades e alertas (com o resultado quando um stop ou alvo é atingido) e depois um resumo do mercado. Clicar leva-te ao sítio certo.", "notifications", null),
        new("Ajuda", "Quando um termo não for claro, passa o rato sobre ele ou abre o glossário. Podes repetir este guia quando quiseres.", "help", "/help")
    ];

    public static readonly IReadOnlyList<FaqItem> Faq =
    [
        new("De onde vêm os preços?", "Do Yahoo Finance (cotações e histórico). Ao vivo, o Finnhub dá trades dos EUA e forex (OANDA) e a Binance dá a cripto. Futuros, índices e ações europeias só têm o Yahoo, com atraso possível."),
        new("Porque é que o Bitcoin aqui é diferente de outro site?", "Não há um preço único: cada bolsa tem o seu e a Binance cota em USDT, que costuma valer cerca de 0,1% acima do dólar. Por omissão a app alinha o ao vivo ao USD do Yahoo. O preço que conta é o da tua corretora."),
        new("A certeza de 80% quer dizer que ganho 8 em 10?", "Não. É uma estimativa do modelo. O acerto real vê-se no track record e na calibração, e costuma ser menor."),
        new("O que é uma sugestão intraday?", "Uma ideia para as próximas horas, baseada em velas de 5 minutos, VWAP, volume e notícias recentes. Expira em 3 horas e tem track record próprio."),
        new("Porque é que o radar está vazio?", "Só analisa mercados abertos. Fora de horas, ao fim de semana ou sem movimentos anormais fica vazio."),
        new("O 'Porque se mexeu?' diz a verdade?", "Mostra causas prováveis ordenadas por proximidade no tempo, relevância e fonte. É uma atribuição, não uma prova, e diz quando só encontra o mercado ou o setor. O texto do modelo local é verificado contra as fontes antes de aparecer."),
        new("Posso ligar a Trading 212?", "A Trading 212 só oferece API para contas Invest e ISA, não para CFD. Podes registar as trades no diário ou importá-las por ficheiro CSV."),
        new("A app executa ordens?", "Não. É só informação e apoio à decisão. Nada aqui é aconselhamento financeiro."),
        new("Porque é que não vejo um ativo?", "Em Configurações, no Perfil, podes esconder classes de ativos. Se o ativo não está na watchlist, adiciona-o em Configurações → Watchlist."),
        new("Como recebo alertas no telemóvel?", "Instala a app ntfy, subscreve o teu tópico e configura-o em Configurações → Notificações. Por omissão só recebes o que é teu (stops e alvos das trades, alertas de preço e regras); as novidades do mercado são opcionais. Com o endereço público do servidor definido, tocar na notificação abre a app no sítio certo."),
        new("Posso ter notificações só para mim?", "Sim. Em Configurações → Perfil define o teu próprio tópico ntfy: os teus alertas vão para ele. No Android, com o endereço público definido, a notificação traz o botão Abrir no Trader. No horário silencioso só os stops atingidos passam."),
        new("Para que servem os limites diários?", "Em Carteira → Calculadora podes definir perda máxima diária e número máximo de trades. Ao atingi-los aparece um aviso no Diário e na Sessão. É um travão para o dia mau, não bloqueia nada."),
        new("O que mostra a análise do diário?", "Resultado por hora, dia da semana, ativo, setup e estado emocional, mais a curva de capital e o drawdown. Só tira conclusões de horas com pelo menos 3 trades."),
        new("Há widgets ou atalhos no telemóvel?", "Não há widgets nativos. Marca ativos com a estrela em Trading e usa o separador Favoritos como lista rápida; podes fixar a app no ecrã inicial.")
    ];

    public static IReadOnlyList<ReadingLine> HowToRead(SuggestionDto suggestion)
    {
        CultureInfo pt = CultureInfo.GetCultureInfo("pt-PT");
        bool buy = suggestion.Direction == TradeDirection.Buy;
        double riskPercent = Math.Abs(suggestion.StopLoss - suggestion.Entry) / suggestion.Entry * 100;
        double rewardPercent = Math.Abs(suggestion.TakeProfit - suggestion.Entry) / suggestion.Entry * 100;
        string horizon = suggestion.Horizon == SuggestionHorizon.Intraday
            ? "Intraday: pensada para as próximas horas e expira ao fim de 3 horas."
            : "Swing: pensada para alguns dias e expira ao fim de 5 dias.";

        List<ReadingLine> lines =
        [
            new("Direção", buy
                ? "COMPRAR: a ideia é que o preço suba. Ganhas se subir, perdes se descer."
                : "VENDER: a ideia é que o preço desça (posição curta, normalmente via CFD). Ganhas se descer, perdes se subir."),
            new("Entrada", $"Preço de referência no momento da sugestão: {suggestion.Entry.ToString("0.#####", pt)}. O preço atual pode já ser diferente; se se afastou muito, o R:R real é pior."),
            new("Stop loss", $"{suggestion.StopLoss.ToString("0.#####", pt)}, a {riskPercent.ToString("0.0#", pt)}% da entrada. Se o preço chegar aqui, a ideia falhou: sais e perdes essa distância (1R)."),
            new("Take profit", $"{suggestion.TakeProfit.ToString("0.#####", pt)}, a {rewardPercent.ToString("0.0#", pt)}% da entrada. É o alvo onde garantes o lucro."),
            new("Risco/recompensa", $"1:{suggestion.RiskReward.ToString("0.0#", pt)}. Por cada 1 que arriscas, o alvo paga {suggestion.RiskReward.ToString("0.0#", pt)}. Com este R:R precisas de acertar mais de {(100 / (1 + suggestion.RiskReward)).ToString("0", pt)}% das vezes para não perderes dinheiro (sem contar custos)."),
            new("Certeza", $"{(suggestion.Confidence * 100).ToString("0", pt)}% é a estimativa do modelo com base nos sinais que concordam. Não é uma probabilidade garantida: compara com o acerto histórico no track record."),
            new("Horizonte", horizon),
            new("Provas", "Cada sinal tem um valor de -1 a +1 e um peso. Barras para a direita favorecem a direção sugerida. Quantos mais sinais independentes concordam, mais fiável; sinais que dependem do mesmo movimento do preço contam como evidência parcial."),
            new("O que fazer", "Usa a calculadora de posição para dimensionar o risco (por exemplo 1% do capital), confirma o preço na tua corretora e decide tu. Se o preço já passou do alvo ou do stop, ignora a sugestão.")
        ];

        return lines;
    }
}
