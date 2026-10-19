using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Trader.Core.Abstractions;

namespace Trader.Infrastructure.News;

public sealed class FinBertSentimentAnalyzer : ISentimentAnalyzer, IDisposable
{
    private const int MaxTokens = 128;
    private const int PositiveIndex = 0;
    private const int NegativeIndex = 1;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _outputName;

    public FinBertSentimentAnalyzer(string modelDirectory)
    {
        _session = new InferenceSession(Path.Combine(modelDirectory, "model.onnx"));
        _tokenizer = BertTokenizer.Create(
            Path.Combine(modelDirectory, "vocab.txt"),
            new BertOptions { LowerCaseBeforeTokenization = true });
        _outputName = _session.OutputMetadata.Keys.First();
    }

    public double? Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        IReadOnlyList<int> ids = _tokenizer.EncodeToIds(text, addSpecialTokens: true);
        long[] inputIds = ids.Count > MaxTokens
            ? ids.Take(MaxTokens - 1).Append(_tokenizer.SeparatorTokenId).Select(i => (long)i).ToArray()
            : ids.Select(i => (long)i).ToArray();

        long[] attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();
        long[] tokenTypeIds = new long[inputIds.Length];
        int[] shape = [1, inputIds.Length];

        List<NamedOnnxValue> inputs = [];
        foreach (string name in _session.InputMetadata.Keys)
        {
            long[] values = name switch
            {
                "input_ids" => inputIds,
                "attention_mask" => attentionMask,
                "token_type_ids" => tokenTypeIds,
                _ => throw new InvalidOperationException($"Unexpected FinBERT input '{name}'.")
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, shape)));
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs, [_outputName]);
        float[] logits = results[0].AsEnumerable<float>().ToArray();

        double max = logits.Max();
        double[] exp = logits.Select(l => Math.Exp(l - max)).ToArray();
        double sum = exp.Sum();

        return (exp[PositiveIndex] - exp[NegativeIndex]) / sum;
    }

    public void Dispose() => _session.Dispose();
}
