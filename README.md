# MiniUnity · Unity Editor headless cho agent

MiniUnity là một "Unity giả" viết trên .NET 8, chạy được code C# của một project Unity 2022.3 và đọc/ghi file của project
(scene, prefab, asset, `.meta`, settings) mà **không cần mở Unity Editor và không cần license**. Mục tiêu: cho agent (hoặc script, CI)
làm việc với project Unity trong container Linux như với Editor thật.

Dùng qua:

- **`ua.sh`**: CLI, mỗi lệnh trả về một object JSON. Tài liệu đầy đủ: [`AGENT.md`](AGENT.md).
- **`mcp.sh`**: MCP server (stdio), mỗi lệnh `ua` là một tool.
- **Host của project**: một project .NET nhỏ tham chiếu `Engine/MiniUnity.csproj`, compile code Editor / game của project cùng engine và
  thêm lệnh riêng (`MiniUnity.Common.props` chứa define / cảnh báo dùng chung; đăng ký điểm mở rộng: `TypeCache.AddHostAssembly`,
  `Debug.EchoPrefixes`, `WorldRenderer.RoundedBoxShaders`; lệnh nào host không có thì chuyển cho `Program.Run` của engine).
  Ví dụ: host dựng / quay video của repo này ở [`Tools/MiniUnityVideo`](../Tools/MiniUnityVideo) (`build.sh`, `record.sh`,
  `menu.sh`…; xem [`Docs/Guide/07_MiniUnity.md`](../Assets/VideoRecreate/Docs/Guide/07_MiniUnity.md)). Các script cùng tên trong thư mục
  này chỉ chuyển tiếp sang đó.

## Làm được / không làm được

| Làm được | Không làm được |
|---|---|
| Đọc / sửa / tạo / xoá scene, Hierarchy, GameObject, Transform; thêm / xoá component, đọc / sửa serialized field | Retarget clip humanoid (muscle), Avatar từ FBX; jacobian / inverse dynamics của ArticulationBody |
| Prefab, prefab instance (override như Unity: modifications, added / removed component và GameObject), ScriptableObject | Input thiết bị thật (chỉ có input kịch bản `--input`); gamepad trong package Input System |
| Asset: tìm kiếm, import (`.meta`), move / copy / delete giữ tham chiếu, GUID, importer settings, ProjectSettings, tags / layers, Build Settings | Pixel trong player loop (callback render có, rasterize theo yêu cầu: screenshot / record); post-processing stack, reflection probe, skybox, lightmap |
| Package Manager: resolve, thêm / xoá, tải source; Addressables: group, entry, label, schema, phân tích dependency | Build player (APK / IPA / exe) |
| Compile C# theo luật của Unity (asmdef, define, platform), lỗi kèm `file:line`, compile tăng dần | API Unity chưa có cài đặt: ném `NotImplementedException("Type.Member")` (xem §Thêm API còn thiếu) |
| Chạy EditMode / PlayMode test (NUnit + Unity Test Framework), Play scene (player loop, coroutine, job system, NativeArray), thu exception | Effect trong AudioMixer (group chỉ có volume, mute, solo, snapshot, exposed parameter; filter component trên GameObject thì có) |
| Mô phỏng physics 3D (theo PhysX, có CCD, WheelCollider, joint projection) và 2D (port Box2D, có TOI / bullet), Animator / Animation (avatar mask, API soạn controller), dòng thời gian audio (WAV / Ogg / MP3, AudioMixer, log + mix WAV), legacy Input theo InputManager và package Input System (backend native), ArticulationBody, humanoid IK, PlayerLoop tuỳ biến, PlayerPrefs, nhiều scene | |
| Phân tích Editor.log / Player.log | |
| Chạy code Editor của project: `[MenuItem]`, `AssetDatabase`, `PrefabUtility`, `EditorSceneManager.SaveScene`, Timeline (`PlayableDirector.Evaluate` thật) | |
| Render phần mềm `Camera.Render()`: uGUI + TextMeshPro, SpriteRenderer, 3D (Standard, bóng đổ, fog) – sai khác Unity ~35–43 dB PSNR; ảnh chụp game (`play --screenshot`) và quay video Timeline ra MP4 (`record.sh`) | |

## Yêu cầu

- Linux / macOS / Windows (Git Bash). `bash`, `curl`, `tar`, `python3`, `git`.
- **Unity Editor 2022.3 đã cài**: chỉ để đọc DLL lấy API, reference assembly và source uGUI; **không chạy** Unity, không cần license.
  `setup.sh` tự tìm trong Unity Hub / `/opt/unity/Editor`; hoặc đặt `UNITY_EDITOR=<…/Editor>`.
- .NET 8 SDK (nếu chưa có, `setup.sh` tự cài vào `.cache/dotnet`).
- Engine compile chung code video của repo này, gồm cả `Assets/Pixel Flow` và `Assets/Block Exit` (nằm sẵn trong repo); thiếu
  một trong các thư mục đó thì `setup.sh` dừng ở bước build.
- `tests/ua_selftest.py` và `tests/run_fixtures.py` không cần project nào: chúng mang ProjectSettings và asset riêng
  (`tests/data/`) và lấy test framework / NUnit / Timeline từ `.cache/packages` mà `setup.sh` tải về.

Trên Linux không có Unity Hub (container, CI): `./setup.sh --download-unity` tải bản cài Editor đúng phiên bản (revision lấy từ
`ProjectVersion.txt`, `--unity-revision` hoặc `config/defaults.cfg`) dạng stream và chỉ giữ phần MiniUnity đọc (~1.6 GB trong `.cache/unity/<version>/Editor`, không cần license);
engine tự tìm ở đó. `./setup.sh --check` liệt kê những gì đã có / còn thiếu / đã cũ mà không sửa gì.

## Dùng độc lập (không nằm trong project Unity)

MiniUnity không cần nằm trong project: nếu thư mục cha không phải project Unity thì phiên bản Unity và phiên bản package lấy từ
`config/defaults.cfg`, hoặc chỉ định bằng tham số:

```bash
./setup.sh --download-unity --unity-version 2022.3.62f2 --unity-revision 7670c08855a9   # hoặc --unity-version "2022.3.62f2 (7670c08855a9)"
UNITY_PROJECT=/path/to/Game ./ua.sh go.tree Assets/Scenes/Main.unity
./mcp.sh /path/to/Game
```

CI có job `standalone` chạy đúng cách này: chép `MiniUnity/` ra ngoài repo rồi setup, selftest và fixture.

## Cài đặt

```bash
cd MiniUnity
./setup.sh            # ~2 phút: tải package, sinh stub, decompile, build
```

`setup.sh` làm:

1. Cài .NET 8 nếu thiếu.
2. Tìm Unity Editor đúng phiên bản: `--unity-version`, nếu không thì `ProjectSettings/ProjectVersion.txt` của project, nếu không có
   project thì `config/defaults.cfg`.
3. Tải source package mà engine biên dịch chung (Timeline, TextMeshPro, Cinemachine, uGUI chép từ Editor, LitMotion Ease) theo `Packages/packages-lock.json` (không có project: `config/defaults.cfg`), cùng test framework và NUnit cho các bài test;
   tarball nào cũng được đối chiếu SHA-1 với registry của Unity.
4. Sinh `Engine/Generated/`: stub cho toàn bộ API Unity, đọc từ metadata DLL của Editor (`tools/stubgen`).
5. Sinh `Engine/Decompiled/`: các kiểu thuần managed của Unity (Vector/Quaternion/Color/Mathf…, Pool, Playable, TextCore, SpookyHash),
   decompile **trên máy bạn** bằng `ilspycmd` (file nào decompile hỏng thì dừng kèm thông báo của ilspycmd).
6. Viết lại phép tính `float` theo ngữ nghĩa Mono (`tools/monofloat`) rồi build `Engine/` (`scripts/build_engine.sh`; Roslyn và
   Mono.Cecil lấy từ NuGet).

Các thư mục sinh ra (`.cache/`, `Engine/Generated/`, `Engine/Decompiled/`, `Engine/Transformed/`) **không commit** (`.gitignore`).
Chạy lại `setup.sh` khi đổi Unity / package.

## Dùng

```bash
./ua.sh help                                            # mọi lệnh + cú pháp
./ua.sh go.tree Assets/Scenes/Main.unity --depth 2      # project mặc định: thư mục cha (UNITY_PROJECT)
UNITY_PROJECT=/path/to/Game ./ua.sh compile
./ua.sh test --mode playmode
./ua.sh play Assets/Scenes/Main.unity --seconds 5 --watch "Player/Health.current"
./mcp.sh /path/to/Game                                  # MCP server; cấu hình client: AGENT.md › MCP server
../Tools/MiniUnityVideo/menu.sh - x                    # liệt kê mọi [MenuItem] của project; ./menu.sh - "<Menu/Path>" để chạy
./validate.sh Assets/Scenes/Main.unity                  # fileID trùng / tham chiếu treo / GUID lạ
./ua.sh play Assets/Scenes/Main.unity --size 1080x1920 --screenshot shot.png@2   # chụp màn hình game
../Tools/MiniUnityVideo/record.sh 110 --jobs 4         # quay video NN ra Exports/NN_Key/NN_Key_en.mp4 (Play mode + ffmpeg)
python3 tests/ua_selftest.py                            # tự kiểm tra (~95 kiểm tra trên project tạm)
```

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `UNITY_PROJECT` | `..` | project Unity (thư mục chứa `Assets/`, `Packages/`) |
| `UNITY_EDITOR` | tự tìm | thư mục Unity Editor đã cài |
| `NOCOMPILE` | `1` cho `ua.sh`, `0` cho script khác | `0` = biên dịch lại MiniUnity trước khi chạy |
| `MONOFLOAT` | `1` | `0` = bỏ pass float Mono (build nhanh hơn, lệch chữ số cuối so với Unity) |
| `MINIUNITY_DRYRUN` | `0` | `1` = chạy code Editor nhưng không ghi / xoá scene, prefab, asset |
| `MINIUNITY_MCP_TIMEOUT` | 900 / 120 | giây tối đa cho một tool call của MCP server (xem AGENT.md › MCP server) |

Quy ước lệnh `ua` (đường dẫn, cách chỉ GameObject, giá trị JSON, tham chiếu object, property path), bảng lệnh đầy đủ,
hành vi compile / test / play và giới hạn runtime: [`AGENT.md`](AGENT.md).

## Cách hoạt động

```
code C# của project (Assets/, Packages/) ─┬─ compile cùng engine trong một host (Tools/MiniUnityVideo: build, record, menu …)
                                          └─ hoặc compile riêng bằng Roslyn như Unity (ua compile) → nạp qua facade (ua play / test)
┌──────────────────────────────────────────────────────────────────────────────┐
│ package source thật: Timeline · uGUI · TextMeshPro · Cinemachine · LitMotion │
├──────────────────────────────────────────────────────────────────────────────┤
│ Engine/Core (viết tay): Object/GameObject/Transform/RectTransform, vòng đời   │
│ edit mode + player loop (Play), component native = "túi" YAML, AssetDatabase, │
│ PrefabUtility, EditorSceneManager, SerializedObject, job system, NativeArray, │
│ renderer phần mềm, FontEngine, PNG · Engine/Core/Agent: lệnh ua + MCP        │
├──────────────────────────────────────────────────────────────────────────────┤
│ Engine/Generated: stub toàn bộ API Unity → throw NotImplementedException(...) │
│ Engine/Decompiled: code managed thật của Unity (toán học, pool, Playable…)   │
└──────────────────────────────────────────────────────────────────────────────┘
        │ Serializer theo luật Unity → YAML writer (quote/fold/float như Unity)
        ▼
*.unity · *.prefab · *.asset · *.playable · .meta
```

- **Serializer** theo luật Unity: field public / `[SerializeField]`, list/array, class `[Serializable]`, ref `{fileID, guid, type}`,
  `Color32`/`Rect`/`AnimationCurve`… đúng định dạng; `m_Script` lấy GUID từ `.cs.meta` (project + package).
- **YAML lossless**: lệnh `ua` sửa trên cây YAML giữ nguyên định dạng gốc, phần không sửa giữ từng byte. Writer tái tạo thói quen của
  Unity: chọn plain / `'…'` / `"…"`, gập dòng ở cột 80, float không số mũ, thứ tự modification của prefab. Đọc rồi ghi lại mọi file YAML
  trong repo này cho kết quả giống hệt.
- **Prefab instance**: Hierarchy của instance được dựng ảo từ prefab nguồn + override; sửa object trong instance sinh `m_Modifications`,
  `m_AddedComponents`, `m_AddedGameObjects`, `m_RemovedComponents`, `m_RemovedGameObjects` như Unity.
- **Compile** (`ua compile`): Roslyn từ .NET SDK; chia assembly và define như Editor (xem AGENT.md › Compile), reference assembly lấy từ
  Editor đã cài. Kết quả ở `Library/MiniUnity/ScriptAssemblies`, cache theo hash nội dung.
- **Chạy code đã compile** (`ua play`, `ua test`): DLL tham chiếu `UnityEngine.*Module` / `UnityEditor.*` được nạp qua **facade** sinh tự
  động (`Library/MiniUnity/Facades`): mỗi facade chỉ chứa type forwarder (kể cả type internal và nested) trỏ về type của MiniUnity. DLL
  mang `IgnoresAccessChecksTo("MiniUnity")` để package dùng API internal của engine như `InternalsVisibleTo` trong Unity.
- **Mono float**: Unity (Mono) tính biểu thức `float` bằng `double` rồi mới làm tròn; `tools/monofloat` viết lại code biên dịch chung
  + toán học theo cách đó để kết quả trùng tới chữ số cuối.
- **`Hash128.Compute`** khớp Unity (SpookyHash V2 trên UTF-8, `ToString` theo thứ tự byte bộ nhớ).

### Render

`Camera.Render()` là renderer phần mềm làm theo canvas native của Unity:

- **Hình học thật**: uGUI (`Graphic.OnPopulateMesh` → `VertexHelper.FillMesh`) và TextMeshPro (`GenerateTextMesh`) chạy code package thật trong
  `Canvas.ForceUpdateCanvases` rồi giao mesh cho `CanvasRenderer.SetMesh`; renderer vẽ lại mesh, material, texture, rect clipping, màu, alpha.
- **Canvas "driven"**: canvas Screen Space theo camera (`planeDistance`, scale theo `orthographicSize`/`fieldOfView`) như Unity.
- **Shader** viết lại từ mã gốc: `UI/Default` (premultiplied, clip rect + softness, alpha clip), `TextMeshPro/Mobile/Distance Field`
  (SDF, outline, underlay), thứ tự vẽ theo hierarchy và `sortingOrder`, CanvasGroup alpha, stencil (`Mask`), `ColorMask`.
- **Màu**: project Linear – texture sRGB giải mã trước khi lọc, màu vertex sang linear rồi lượng tử 8-bit, blend trong linear.
- `RenderTexture`, `Texture2D.ReadPixels/GetPixelBilinear/SetPixel(s)`, giải mã PNG, atlas Alpha8 trong font asset.
- **FontEngine**: đọc TrueType, metric kiểu FreeType không hint, SDF từ khoảng cách thật tới đường viền, xếp atlas như Unity → font asset
  động tự thêm glyph mới (chỉ trong bộ nhớ). `mu fonttest <root> <font.asset>` so với glyph Unity đã tạo.
- **3D**: rasterizer có Z-buffer; `Standard` theo BRDF1 của Unity (GGX), ambient Trilight → SH, fog Linear, Fade transparency;
  bóng directional theo cascade của `QualitySettings`, lọc Soft / Hard, bias theo `Light`; đèn phụ không bóng; MSAA bằng supersampling.
  Mesh primitive theo fileID builtin của Unity; mesh `.asset` đọc từ `m_VertexData`.
- **SpriteRenderer** vẽ cùng canvas Screen Space – Camera, sắp theo sorting layer / order / khoảng cách như Unity.

### Quay video (`Tools/MiniUnityVideo/record.sh`)

`./record.sh <NN> [--lang en|vi] [--fps 30] [--size 1920x1080] [--from s] [--to s] [--subtitles 0|1] [--jobs N] [--out file.mp4]`
dựng video trong bộ nhớ (không ghi scene), chạy Play mode với `PlayableDirector` tiến theo thời gian game như `Time.captureFramerate`
của Unity Recorder, render mọi camera mỗi frame và đẩy vào ffmpeg; âm thanh = mix AudioTrack của Timeline (thoại theo ngôn ngữ, BGM)
cộng âm thanh game. `--jobs N` chia đoạn cho N process (mỗi process chạy im lặng tới đầu đoạn của nó rồi mới render), sau đó nối lại.
Tốc độ ~1.5 fps / job ở 1080p.

## Thêm API còn thiếu

`NotImplementedException("Type.Member")` (trong `ua play` / `ua test` được đánh dấu `"miniunity"`) nghĩa là code vừa gọi một API Unity
chưa có cài đặt. `./ua.sh apigap` liệt kê trước mọi API như vậy mà project gọi (xếp theo số chỗ gọi, kèm `file:line`). Cách thêm:

1. Viết `partial` của kiểu đó trong `Engine/Core/` (vd. `Objects/Patches.cs`):
   ```csharp
   namespace UnityEngine { public partial class Texture2D { public bool alphaIsTransparency { get; set; } } }
   ```
   Không ghi `static`/`sealed` trên partial. `scripts/gen_stubs.sh` tự bỏ member trùng tên (kể cả operator) khỏi stub (`tools/handmembers.py`).
   Kiểu lồng (nested) không được nhận tự động: thêm dòng `Ns.Outer.Inner: *` vào `config/stubs.manual.cfg`.
2. Thay hẳn một kiểu: `-Kiểu.Đầy.Đủ` trong `config/stubs.manual.cfg` rồi viết kiểu đầy đủ (vd. `NativeContainers.cs`).
3. API chỉ cần "không làm gì": `~Kiểu.Đầy.Đủ` (mọi method trả `default`).
4. Kiểu Unity thuần managed: thêm vào `config/managed_types.txt` (`Module Kiểu`) để decompile bản thật.
5. Component native mới (vd. `Light`): class id trong `Serializer.nativeIds`, template YAML trong `NativeTemplates`, property map vào
   `m_Native` (xem `NativeComponents.cs`); `ClassIds.cs` cho lệnh `ua`.
6. `source env.sh && scripts/gen_stubs.sh "$UNITY_EDITOR/Data"`, build lại, chạy `python3 tests/ua_selftest.py`.

## Giấy phép

Phần commit trong thư mục này chỉ gồm code viết tay và công cụ. Mọi phần **dẫn xuất từ Unity** (stub sinh từ DLL, code
decompile, uGUI source chép từ Editor) và package Unity tải về đều được **tạo trên máy người dùng** từ bản Unity của chính họ,
nằm trong `.gitignore`. Kiểm tra điều khoản Unity (Unity Companion License / EULA) trước khi phân phối lại các phần đó.

## Tách thành submodule

```bash
# tách lịch sử thư mục MiniUnity thành repo riêng
git subtree split --prefix=MiniUnity -b miniunity-split
#   (hoặc: git clone <repo> mu && cd mu && git filter-repo --subdirectory-filter MiniUnity)
git push git@github.com:<org>/MiniUnity.git miniunity-split:main
# thay thư mục bằng submodule
git rm -r MiniUnity && git commit -m "Remove MiniUnity (moving to submodule)"
git submodule add git@github.com:<org>/MiniUnity.git MiniUnity
```

Các script dùng đường dẫn tương đối và `UNITY_PROJECT` (mặc định `..`), nên submodule đặt ở gốc repo chạy được ngay. Engine build
không cần code của repo này (`scripts/build_engine.sh`); phần riêng của repo nằm ở host `Tools/MiniUnityVideo`, tham chiếu
`../../MiniUnity/Engine/MiniUnity.csproj`. Trước khi tách, xoá các script chuyển tiếp (`build.sh`, `produce.sh`, `record.sh`,
`thumb.sh`, `sheet.sh`, `menu.sh`, `layout.sh`, `parts.sh`, `partlib.sh`, `compare.sh`) và cập nhật tài liệu sang `Tools/MiniUnityVideo/`.
