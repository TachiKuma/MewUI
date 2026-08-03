# 텍스트 뷰 확장

`MultiLineTextBox`와 `SyntaxViewer`는 컨트롤을 상속하거나 렌더링 코드를 수정하지 않고도 텍스트 표시를 확장할 수 있습니다. 검색 결과에 배경색을 칠하고, 오류 구간에 물결선을 긋고, 공백 문자를 기호로 바꿔 보여주고, 특정 줄을 접어서 숨기는 기능을 확장 객체를 등록하는 것만으로 추가합니다. MewvalonEdit 확장의 `TextEditor`도 같은 파이프라인을 사용하며 `editor.TextArea.TextView.Extensions`로 등록합니다.

## 첫 예제: 검색 하이라이트

가장 흔한 확장은 "문자 범위에 색 입히기"입니다. `ITextClassifier`를 구현해 `Extensions.Classifiers`에 등록하면 됩니다.

```csharp
using Aprillz.MewUI.Text;

sealed class SearchHighlighter : ITextClassifier
{
    private static readonly Color _matchColor = Color.FromArgb(88, 255, 214, 0);

    public List<int> Matches { get; } = new();   // 문서 절대 오프셋
    public int QueryLength { get; set; }

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        int lineStart = context.LogicalLine.Offset;
        int lineEnd = lineStart + context.LogicalLine.Length;

        foreach (int matchStart in Matches)
        {
            if (matchStart >= lineEnd) break;
            int start = Math.Max(lineStart, matchStart);
            int end = Math.Min(lineEnd, matchStart + QueryLength);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Background: _matchColor));
            }
        }
    }
}
```

```csharp
var highlighter = new SearchHighlighter();
editor.Extensions.Classifiers.Add(highlighter);

// 검색어가 바뀌면: Matches를 다시 채우고 다시 그리게 합니다.
highlighter.Matches.Clear();
// ... 문서 텍스트에서 매치 오프셋 수집 ...
editor.InvalidateTextView();
```

`Classify`는 화면에 보이는 줄에 대해서만 호출되므로, 매치 검색 같은 무거운 계산은 미리 해 두고 콜백에서는 결과 조회만 합니다. 세브론 이동까지 포함한 동작하는 예제가 갤러리 Inputs 페이지의 "Find Highlight" 카드에 있습니다 (`samples/MewUI.Gallery/GalleryView.Input.cs`).

## 어떤 확장을 쓰나

하고 싶은 일에 따라 등록하는 목록이 다릅니다. 모두 `Extensions` (`TextViewExtensionPipeline`) 아래에 있습니다.

| 하고 싶은 일 | 확장 | 등록 목록 |
| --- | --- | --- |
| 문자 범위에 전경색/배경색/밑줄 | `ITextClassifier` | `Classifiers` |
| 물결선, 괄호 강조 등 임의 드로잉 | `ITextAdornmentProvider` | `AdornmentProviders` |
| 표시 텍스트 치환 (공백 기호, 접기 자리표시자) | `ITextProjection` | `Projections` |
| 줄 안에 인라인 요소 삽입 | `ITextElementGenerator` | `ElementGenerators` |
| 굵기/크기처럼 글자 배치가 변하는 스타일 | `ITextLineTransformer` | `Transformers` |
| 특정 줄을 표시에서 제외 | `ITextLineCollapser` | `LineCollapsers` |

색만 바꾸는 데는 분류기를 쓰고, 변환기는 글자 폭과 줄바꿈에 영향을 주는 변경에만 씁니다. 분류기의 색 변경은 레이아웃을 다시 계산하지 않습니다.

## 색과 장식: TextPaintSpan

분류기가 출력하는 `TextPaintSpan`은 줄 상대 오프셋의 범위와 스타일 묶음입니다.

```csharp
public readonly record struct TextPaintSpan(
    TextRange Range,
    Color? Foreground = null,
    Color? Background = null,
    TextDecoration Decoration = TextDecoration.None);
```

스팬이 겹치면 나중에 등록한 분류기가 이깁니다. 배경은 등록 순서대로 그려져 뒤가 위에 오고, 전경색도 나중 스팬이 덮어씁니다. 내장 하이라이팅과 함께 쓸 때의 우선순위도 등록 순서로 조절합니다.

## 임의 드로잉: adornment

스팬으로 표현할 수 없는 모양(물결선, 테두리, 연결선)은 `ITextAdornmentProvider`가 줄마다 `ITextAdornment`를 내놓는 방식으로 그립니다. `Draw`는 줄 레이아웃(`TextLineLayout`)을 받으므로 문자 범위의 실제 위치를 조회해 정확한 좌표에 그릴 수 있습니다.

`Layer`가 그리기 순서를 정합니다. 한 줄은 `Background` 장식, glyph와 페인트 스팬, `Text` 장식, `Foreground` 장식 순으로 그려집니다.

- `TextAdornmentLayer.Background`: glyph 아래. 블록 배경, 현재 줄 강조
- `TextAdornmentLayer.Text`: glyph 바로 위
- `TextAdornmentLayer.Foreground`: 최상단. 물결선, 취소선류 장식

## 표시 텍스트 치환: projection

`ITextProjection`은 줄의 표시 텍스트 자체를 바꿉니다. 탭/공백을 `·` 같은 기호로 치환하거나, 접힌 코드 구간을 `...` 자리표시자로 줄이는 데 씁니다.

```csharp
public interface ITextProjection
{
    ProjectedText Project(in TextProjectionContext context);
}

public readonly record struct ProjectedText(ReadOnlyMemory<char> Text, ITextOffsetMap OffsetMap);
```

projection은 반드시 `ITextOffsetMap`을 함께 반환해야 합니다. 표시 텍스트와 문서 텍스트의 오프셋을 서로 변환하는 맵으로, 길이가 변하지 않는 1:1 치환이면 `IdentityTextOffsetMap.Instance`를 그대로 쓰면 됩니다.

치환으로 길이가 변하면 표시 오프셋과 문서 오프셋이 어긋나므로, 문서 오프셋 기반 데이터(검색 매치, 진단 범위)를 그리는 분류기/장식은 컨텍스트로 전달되는 `OffsetMap`의 `MapFromSource`(문서에서 표시로)로 범위를 변환한 뒤 출력해야 합니다. 이렇게 하면 접기 같은 projection과 하이라이트가 올바르게 공존합니다.

줄 전체를 숨기려면 projection이 아니라 `ITextLineCollapser`를 씁니다. 접기 기능은 보통 첫 줄을 자리표시자로 치환하는 projection과 후속 줄을 숨기는 collapser의 조합입니다.

## 언제 다시 그려지나

확장 콜백은 화면에 보이는 줄이 레이아웃될 때만 실행됩니다. 스크롤로 새 줄이 보이거나, 문서가 바뀌거나, `InvalidateTextView()`를 호출했을 때입니다. 이 실행 모델에서 두 가지 규칙이 나옵니다.

- 문서가 바뀌면 뷰는 알아서 갱신됩니다. 확장이 캐시(파싱 결과, 매치 목록)를 들고 있다면 그 캐시만 문서 변경에 맞춰 갱신하면 됩니다. 두 컨트롤 모두 내용 변경과 문서 교체를 통지하는 `DocumentChanged` 이벤트를 제공하므로 캐시 갱신 시점으로 쓰면 됩니다.
- 확장 자신의 상태가 바뀌었을 때(검색어 변경, 하이라이팅 규칙 교체, 등록 추가/제거)는 뷰가 알 수 없으므로 `InvalidateTextView()`를 직접 호출합니다.

콜백 안에서 문서 전체를 파싱하지 마십시오. 파싱은 문서 변경 시점에 한 번 해서 결과를 보관하고, 콜백은 줄과 교차하는 부분을 조회만 하는 구조가 맞습니다.

`MultiLineTextBox.Document`에 새 문서를 할당해도 확장 등록과 뷰는 유지되므로(caret, 선택, 스크롤, undo는 초기화) 문서 교체 시 확장을 다시 등록할 필요가 없습니다.
