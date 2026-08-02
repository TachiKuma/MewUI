namespace MewvalonEdit.Sample;

internal static class SampleText
{
    public const string CSharp = """
        using System.Collections.Generic;
        using System.Linq;

        namespace MewalonEdit.Sample;

        [Obsolete("Use CreateAsync instead")]
        public sealed record Result(int Id, string Name);

        public static class ResultService
        {
            // This sample exercises keywords, types, strings, numbers, and interpolation.
            public static async Task<IReadOnlyList<Result>> CreateAsync(
                IEnumerable<string?> names,
                CancellationToken cancellationToken = default)
            {
                const int minimumLength = 3;
                await Task.Delay(42, cancellationToken);

                return names
                    .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length >= minimumLength)
                    .Select((name, index) => new Result(index + 1, $"Item {index}: {name!.Trim()}"))
                    .ToArray();
            }
        }
        """;

    public const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalog generated="true">
          <!-- XML highlighting and editing use the same text surface. -->
          <book id="42" language="ko-KR">
            <title>MewalonEdit</title>
            <author>Aprillz</author>
          </book>
        </catalog>
        """;

    public const string Json = """
        {
          "editor": "MewalonEdit",
          "enabled": true,
          "features": ["highlighting", "folding", "search"],
          "limits": { "lines": 1000000, "wrap": false }
        }
        """;

    public static string LongDocument()
        => string.Join('\n', Enumerable.Range(1, 20_000)
            .Select(index => $"// line {index:D5}: public static int Value{index} => {index};"));
}
