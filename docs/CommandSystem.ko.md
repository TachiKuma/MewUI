# Command System

MewUI의 Command System은 키보드, 버튼, 메뉴, 코드 호출을 하나의 의미 기반 실행 경로로 통합합니다.
명령의 정체성(`Command`), 실행 위치(`CommandScope`), 입력 제스처(`InputMap`), 표시 컨트롤을 서로 분리합니다.

```text
InputMap / Button / Menu
          ↓
       Command
          ↓
    CommandRouter
          ↓
 CommandScope의 CanExecute / Execute
```

## 기본 구성

`Command`는 동작의 정체성과 기본 표시 텍스트만 가집니다. 실행 delegate는 `CommandScope`에,
키 제스처는 `InputMap`에 등록합니다. 같은 `Id`를 사용해도 서로 다른 `Command` 인스턴스는 다른 명령입니다.

```csharp
var save = new Command("file.save", "Save");

window.Commands.Bind(save, () => document.Save(), () => document.IsDirty);
window.InputMap.Bind(save, new KeyGesture(Key.S, ModifierKeys.Primary));
```

`CommandScope.Bind`가 반환하는 `CommandRegistration`을 dispose하거나 `Unbind`를 호출하면 바인딩이 제거됩니다.
한 scope에는 명령당 하나의 바인딩만 둘 수 있습니다.

## C# Markup에서 사용

Button은 `Command(...)` 또는 `BindCommand(...)`로 의미 동작에 연결합니다.

```csharp
new Button()
    .Content("Save")
    .Command(save)
```

메뉴도 callback이나 자체 shortcut을 소유하지 않습니다. `Command.Text`를 그대로 쓰거나 표시 텍스트를 덮어쓸 수 있습니다.

```csharp
var fileMenu = new Menu()
    .Item(save)
    .Item("Save As...", saveAs)
    .Separator()
    .Item("Unavailable", isEnabled: false); // 표시 전용 항목
```

메뉴의 shortcut 열은 현재 command target에서 실제로 유효한 `InputMap` 제스처를 역조회해 표시합니다.
따라서 메뉴에 별도 shortcut을 중복 선언하지 않습니다.

Command 아이콘은 표시 위치가 요청한 DIP 크기로 새 visual을 만드는 `IconTemplate`로 정의합니다.
ContextMenu와 MenuBar dropdown은 16 DIP를 사용하며, 향후 Toolbar presenter의 기본 크기는 24 DIP입니다.

```csharp
var copyGeometry = PathGeometry.Parse(copyPathData);
copyGeometry.Freeze();

var copyIcon = new IconTemplate(
    size => new PathShape()
        .Data(copyGeometry)
        .Size(size)
        .Stretch(Stretch.Uniform));

var copy = new Command("edit.copy", "Copy", copyIcon);
```

`IconTemplate.Build`는 호출될 때마다 parent가 없는 새로운 `FrameworkElement`를 반환해야 합니다. 따라서
여러 presenter가 같은 Command를 동시에 표시해도 visual parent가 충돌하지 않습니다. `ImageSource`,
`SvgImageSource`, freeze한 `PathGeometry`처럼 visual이 아닌 리소스는 factory 밖에서 만들어 공유합니다.
SVG는 새 `Image`, geometry는 새 `PathShape`, emoji는 새 `TextBlock`을 반환하는 방식으로 사용합니다.
렌더링 프레임이나 `CanExecute` 평가마다 생성하는 것이 아니라 presenter가 만들어질 때 한 번 생성합니다.

현재 Core에서 Command 아이콘을 자동 materialize하는 presenter는 ContextMenu와 MenuBar dropdown입니다.
일반 Button은 기존처럼 `Content`를 명시적으로 구성하며, Toolbar presenter는 아직 제공하지 않습니다.
`ThemeMetrics.ToolBarIconSize`의 24 DIP는 해당 presenter를 추가할 때 사용할 계약을 미리 고정한 값입니다.

MenuItem별로 Command 아이콘을 덮어쓸 수도 있습니다.

```csharp
new MenuItem("_Copy", copy)
    .Icon(compactCopyIcon);
```

## 라우팅과 scope

Element와 Window는 각각 `Commands`와 `InputMap`을 제공합니다. 실행 시 라우터는 현재 target에서 시작해
요소의 command context, Window, Application 순으로 바인딩을 찾습니다. `CommandScope.Parent`를 사용하면
visual tree와 독립적인 의미 scope 체인도 만들 수 있습니다.

가장 가까운 scope에 바인딩이 있으면 그 바인딩이 명령을 소유합니다. `CanExecute`가 `false`여도 더 먼
scope의 같은 명령으로 fallback하지 않습니다. 키 제스처 역시 가장 가까운 `InputMap`이 의미를 결정합니다.

명시적인 scope를 사용하는 동적 ContextMenu는 target을 함께 지정합니다.

```csharp
var menu = new ContextMenu();
var scope = new CommandScope();
var select = new Command("document.select", "Select");

scope.Bind(select, SelectDocument, CanSelectDocument);
menu.Item(select);
menu.SetCommandTarget(CommandTarget.From(scope));
menu.ShowAt(owner, position);
```

## 표준 편집 명령

`StandardCommands`는 `Cut`, `Copy`, `Paste`, `Delete`, `Undo`, `Redo`, `SelectAll`을 제공합니다.
TextBox 계열은 이 명령을 자신의 scope에 바인딩합니다. 기본 키는 Application `InputMap`에 등록되므로
로컬 또는 Window `InputMap`에서 재매핑하거나 shadow할 수 있습니다.

```csharp
editor.InputMap.Bind(StandardCommands.Copy, new KeyGesture(Key.Insert, ModifierKeys.Control));
```

## TextBox, ContextMenu, InputMap과 Edit 메뉴

다음 그림은 타입 상속 관계나 실제 visual tree를 나타내지 않습니다. 같은 편집 의미가 여러 UI 진입점에서
어떻게 하나의 명령 실행으로 합쳐지는지 보여 주는 **논리적 구성도**입니다.

```text
키보드 Primary+X/C/V
  └─ Application InputMap ───────────────┐
                                         │
TextBox 우클릭 ContextMenu                ├─ StandardCommands.Cut/Copy/Paste
  └─ Cut / Copy / Paste 메뉴 항목 ───────┤             │
                                         │             ▼
MenuBar의 Edit 메뉴                       │    현재 command target에서
  └─ Cut / Copy / Paste 메뉴 항목 ───────┘    TextBox의 binding 실행
                                                       │
                                                       ▼
                                               선택 영역/클립보드 변경
```

`TextBox`는 생성될 때 `Cut`, `Copy`, `Paste`의 실행과 `CanExecute`를 자신의 `Commands`에 바인딩합니다.
Application의 기본 `InputMap`은 `Primary+X`, `Primary+C`, `Primary+V`를 각각 같은 표준 명령으로 변환합니다.
ContextMenu와 Edit 메뉴는 실행 delegate를 따로 갖지 않고 그 `Command`만 참조합니다.

아래는 관계를 명시적으로 드러내기 위해 TextBox에 사용자 정의 ContextMenu를 붙인 예제입니다. ContextMenu를
지정하지 않으면 TextBox가 같은 표준 명령들로 구성된 기본 편집 메뉴를 필요할 때 생성합니다.

```csharp
var editor = new TextBox()
    .Text("Select text, then use Cut or Copy.")
    .ContextMenu(
        new ContextMenu()
            .Item(StandardCommands.Cut)
            .Item(StandardCommands.Copy)
            .Item(StandardCommands.Paste));

var editMenu = new Menu()
    .Item(StandardCommands.Cut)
    .Item(StandardCommands.Copy)
    .Item(StandardCommands.Paste);

var menuBar = new MenuBar()
    .Items(new MenuItem("_Edit").Menu(editMenu));

// menuBar와 editor를 같은 Window의 레이아웃에 배치한다.
```

이 예제에서 메뉴 객체가 TextBox를 상속하거나 TextBox의 command binding을 복제하는 것은 아닙니다.
실제 연결은 메뉴를 열거나 키를 누른 시점의 **command target**으로 결정됩니다.

- 키보드: 포커스된 TextBox에서 시작해 유효한 `InputMap`을 찾습니다. Application의 기본 매핑이
  `Primary+X/C/V`를 표준 명령으로 바꾸고, 라우터가 포커스된 TextBox의 binding을 실행합니다.
- TextBox ContextMenu: 우클릭 owner인 TextBox를 target으로 캡처합니다. 따라서 메뉴가 포커스를 가져가더라도
  원래 TextBox의 선택 영역을 대상으로 명령을 실행합니다.
- MenuBar의 Edit 메뉴: 메뉴를 열기 직전의 포커스 target을 보존합니다. TextBox에 포커스가 있었다면
  Edit 메뉴의 `Cut`, `Copy`, `Paste`도 그 TextBox로 라우팅됩니다.

세 경로는 같은 `CanExecute` 결과도 공유합니다. 예를 들어 선택 영역이 없으면 TextBox의 `Cut`과 `Copy`
binding이 실행 불가이므로 ContextMenu와 Edit 메뉴에서 모두 비활성화됩니다. `IsReadOnly`인 TextBox에서는
`Cut`과 `Paste`가 비활성화됩니다. 메뉴의 shortcut 표시는 현재 target에서 유효한 `InputMap`을 역조회하므로,
키를 재매핑해도 ContextMenu와 Edit 메뉴에 별도 shortcut 문자열을 수정할 필요가 없습니다.

## CanExecute와 상태 갱신

`CanExecute`는 부작용이 없는 빠른 함수여야 합니다. MewUI는 연결된 Button과 열린 메뉴 같은 활성 command source만
추적하여 dispatcher turn이 끝날 때 상태를 평가합니다. Focus, property, input-map 변경에도 다시 평가합니다.
실행 직전에는 표시 상태와 관계없이 `CanExecute`를 다시 확인합니다.

일반 필드처럼 프레임워크가 변경을 관찰할 수 없는 상태를 UI 스레드 밖에서 바꾼 경우에는 UI dispatcher로
변경을 전달해야 합니다. 임의의 전체 visual tree scan이나 명령별 변경 이벤트는 기본 모델에 포함되지 않습니다.

## 수명 관리

Button은 visual root에 연결된 동안에만 command source로 추적됩니다. ContextMenu는 열린 동안만 추적됩니다.
Window가 닫히면 해당 Window의 source tracker가 비워집니다. 장기 생존 scope에 임시 handler를 등록했다면
`CommandRegistration`을 dispose해 캡처한 객체가 불필요하게 유지되지 않도록 합니다.

## 제거된 레거시 API

다음 경로는 Command System과 중복 실행 또는 서로 다른 활성 상태를 만들기 때문에 제거되었습니다.

- `Window.KeyBindings`, `Window.ProcessKeyBindings`, core `KeyBinding`
- `Button.CanClick`, C# Markup `OnCanClick`
- `MenuItem.Click`, `MenuItem.CanClick`, `MenuItem.Shortcut`
- callback 기반 `Menu.Item`/`ContextMenu.Item` 및 shortcut 인자

단순 UI 클릭 이벤트인 `Button.Click`/`OnClick`은 그대로 사용할 수 있지만, 재사용 동작·활성 조건·단축키·메뉴와
공유되는 동작에는 Command를 사용합니다.

## 아이콘 수명과 크기

`Command.Icon`과 `MenuItem.Icon`의 타입은 `IconTemplate?`입니다. `MenuItem.Icon`이 null이면 Command 아이콘을
사용합니다. ContextMenu는 열릴 때 각 command item의 template을 16 DIP로 build하고 닫힐 때 생성한 visual의
parent를 해제합니다. 다시 열면 새 visual을 만듭니다.

factory에 전달되는 크기는 pixel이 아닌 DIP입니다. DPI 변환과 disabled opacity는 presenter가 담당합니다.
아이콘 source를 factory 안에서 매번 파싱하지 말고 공유 가능한 source를 캡처해야 합니다. factory가 반환한
element는 presenter가 정사각형 slot으로 제한하므로 vector와 bitmap에는 `Stretch.Uniform`을 권장합니다.
