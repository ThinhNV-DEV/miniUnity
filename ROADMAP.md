# MiniUnity · Phân tích hiện trạng và kế hoạch cải thiện

Ngày phân tích: 2026-10-15 (commit `083bbf0`). Tài liệu này là kế hoạch, chưa có thay đổi code nào kèm theo.

## 1. Hiện trạng

**Quy mô.** Khoảng 32.5k dòng C# viết tay trong `Engine/Core` và `Engine/Shims`. Phần sinh ra (`Generated/`, `Decompiled/`,
`Transformed/`) không commit. Không có comment TODO, FIXME hay HACK nào; các chỗ còn thiếu nằm ở stub no-op và ở giới hạn đã
ghi trong README và AGENT.md.

| Mảng | Dòng | Đã làm tốt | Điểm yếu chính |
|---|---|---|---|
| `ua` CLI và MCP (`Agent/`) | ~5k | 75 lệnh, JSON ổn định, YAML lossless, prefab override như Unity, compile như Unity, `batch` | Mỗi lần gọi MCP tạo một process mới (cold start); không có timeout; MCP schema chung cho mọi tool; 22 lệnh chưa có test |
| Physics 3D | ~4.8k | PGS, CCD, joint, khớp có projection, wheel, CharacterController, layer matrix, trigger | Broadphase sort LINQ mỗi bước (X-only SAP); query quét tuyến tính; `excludeLayers`/`includeLayers` của collider bị bỏ qua; mesh–mesh không sinh contact |
| Physics 2D (Box2D) | ~4.9k | Bản port gần đủ: TOI, joint, effector, `ContactFilter2D` | Không có dynamic tree; thứ tự contact theo `Dictionary`; `CompositeCollider2D.GetPath` trả 0; không có `TilemapCollider2D` |
| Animation | ~3.3k | State machine, blend tree, layer, IK, avatar mask | Blend quaternion không kiểm tra bán cầu; additive tính sai pose tham chiếu; không retarget humanoid; cấp phát và reflection mỗi frame |
| Audio | ~0.9k | DSP clock, mixer volume/snapshot, mix ra WAV | Ogg/MP3 phụ thuộc ffmpeg; không có effect, solo, voice limit, `OnAudioFilterRead` |
| Input | ~0.8k | Legacy Input bám InputManager; Input System qua backend native | Input System không có Gamepad/Joystick |
| Render | ~1.9k | uGUI + TMP thật, Standard BRDF, bóng cascade, PSNR 35–43 dB | Đơn luồng, scalar, không SIMD; shader 3D không đọc texture albedo; ~1.5 fps/job ở 1080p |
| Serializer / YAML | ~1.5k | Round-trip giống hệt trên toàn repo | Parser dựa trên dòng (`--- ` trong scalar nhiều dòng bị coi là ngắt document); 6 `catch {}` nuốt lỗi; không ghi được prefab lồng nhau hay prefab variant |
| Setup / build | | `setup.sh` một bước | Cần đúng phiên bản Unity Editor cài sẵn; tải mạng không có checksum; `ilspycmd` lỗi bị nuốt (`2>/dev/null`); Roslyn tham chiếu qua đường dẫn nội bộ của SDK |
| CI | | Có `ua_selftest.py` và 18 fixture test | Không có CI; nhiều test cần Editor; `InputTests` bị `Assert.Ignore` khi chạy mặc định |

### Rủi ro cụ thể đã kiểm tra lại

- **An toàn ghi file:** `SafeIO.Check` dùng `full.StartsWith(root)` mà không thêm dấu phân cách
  (`Engine/Core/Assets/AssetLoader.cs:86`). Với root `/proj`, đường dẫn `/proj2/...` vẫn qua được bước kiểm tra.
- **Ghi không nguyên tử:** `UFile.Save` ghi đè trực tiếp lên file. Nếu process chết giữa chừng, scene hoặc prefab bị hỏng.
- **MCP treo:** `RunChild` (`Agent/UaMcp.cs:182-197`) không có timeout và bỏ qua `notifications/cancelled`. Một lệnh `play`
  bị treo sẽ khoá cả server.
- **Batch:** một dòng JSONL lỗi bị ném ra ngoài try/catch (`UaCommands.cs:71-73`), nên output không còn là JSON.
  `UaArgs.I` dùng `int.Parse` nên trả về `FormatException` thô kèm stack.
- **Mất dữ liệu âm thầm:** `Serializer.cs` có `catch { v = null; }` ở dòng 276, các `catch {}` ở 317, 318, 416, 594, và
  exception khi deserialize bị hạ xuống warning (537).
- **Engine dính với project video:** `MiniUnity.csproj:31-35` compile thẳng `Assets/VideoRecreate`, `Pixel Flow` và
  `Block Exit`. `Program.cs` gọi `VideoPipeline`/`VideoBuilder`. `WorldRenderer` xử lý riêng shader `VideoRecreate/RoundedBox`.
  Vì vậy engine không build được độc lập, trái với mục "Tách thành submodule" trong README.

## 2. Mục tiêu

1. **Tin cậy:** không làm hỏng project của người dùng; mọi lỗi đều trả về JSON có ích.
2. **Nhanh cho agent:** lệnh đọc qua MCP dưới 200 ms; `play`/`test` không phải dựng lại trạng thái đã có.
3. **Độc lập:** engine build và test được mà không cần code video; có CI.
4. **Sát Unity hơn** ở những chỗ project thật hay chạm tới: layer filter, Input System gamepad, prefab lồng nhau, texture 3D.

## 3. Kế hoạch theo giai đoạn

Ước lượng tính theo ngày làm việc của một người (hoặc một phiên agent tương đương).

### Giai đoạn 0 · Sửa lỗi an toàn và độ tin cậy (2–3 ngày, làm trước) · ĐÃ LÀM

> Trạng thái: đã cài đặt cả 7 mục và kiểm tra với Unity 2022.3.62f2 (bản Linux, chỉ phần `Data/` cần cho `setup.sh`):
> `setup.sh` + build engine 0 lỗi (7 warning, đều có từ trước) với Pixel Flow nằm trong repo; compile cả repo 149 assembly 0 lỗi;
> `tests/ua_selftest.py` 103/103; 96/96 fixture test runtime (`tests/fixtures`, gồm InputTests với `--input`); 23/23 test Block Exit;
> đọc–ghi lại 277 file YAML của repo giống hệt từng byte; timeout MCP kill được `ua test` thật sau 5 s mà không để lại process con.
> Test Pixel Flow (151): engine trước Phase 0 và sau Phase 0 lỗi đúng cùng 17 test (Phase 0 không gây hồi quy); sau đó sửa
> `JsonUtility` (JSON hỏng → `ArgumentException` như Unity) và cài `RenderParams` / `Graphics.RenderMeshInstanced` → 139/151.
> 12 test còn lại cần hai mục ở Giai đoạn 4 (mục 10, 11). Khác với dự kiến:
> - 0.3: timeout mặc định là 900 s cho `compile` / `test` / `play` / `pkg.resolve` / `pkg.add` / `batch` và 120 s cho lệnh khác
>   (không suy từ `--seconds`, vì thời gian mô phỏng không tương ứng với thời gian thật); tool nhận `timeoutSeconds`.
> - 0.6: header hỏng ném `FormatException` có `file:dòng` (YAML reader không phụ thuộc vào lớp `ua`); `ua` trả nó dưới dạng `error`.
> - 0.7: `pkg.resolve` vốn đã không được đánh dấu read-only, chỉ `play` cần sửa. `go.find --name` giờ không phân biệt hoa thường
>   và hỗ trợ `?` giống `asset.find`. Ngoài kế hoạch: `batch --dry-run` trước đây bị bỏ qua (chỉ `--dry-run` của từng dòng có
>   tác dụng); giờ áp dụng cho mọi dòng.

| # | Việc | Vị trí | Tiêu chí xong |
|---|---|---|---|
| 0.1 | `SafeIO.Check`: so sánh với `root + Path.DirectorySeparatorChar` (hoặc `Path.GetRelativePath` không bắt đầu bằng `..`) | `Assets/AssetLoader.cs:86` | Test: ghi vào `/proj2` khi root là `/proj` bị từ chối |
| 0.2 | Ghi nguyên tử: ghi ra file tạm cùng thư mục rồi `File.Move(overwrite: true)` | `UFile.Save`, `AssetDb.cs:29-35` | Kill process giữa lúc ghi không để lại file cụt |
| 0.3 | MCP: thêm timeout cho từng lệnh (mặc định 120 s, `play`/`test` lấy theo `--seconds` cộng thêm), kill cả cây process, xử lý `notifications/cancelled` | `UaMcp.cs:64, 182-197` | Test với một scene có vòng lặp vô hạn: server vẫn trả lời các lệnh sau |
| 0.4 | `batch`: bắt lỗi parse cho từng dòng; thêm `--stop-on-error`; `UaArgs.I` trả về `UaError` có tên option | `UaCommands.cs:39, 67-80` | Output luôn là JSON hợp lệ |
| 0.5 | Serializer: thay `catch {}` bằng việc ghi vào danh sách `warnings[]` có field và type; thêm `--strict` để biến chúng thành lỗi | `Serialization/Serializer.cs` | Không còn `catch {}` trống; warning hiện trong JSON kết quả |
| 0.6 | YAML reader: chỉ nhận ngắt document khi đúng mẫu `--- !u!<id> &<fileID>`, và trả `UaError` thay vì `int.Parse` ném lỗi | `Yaml/YamlReader.cs:26-32` | Test với scalar nhiều dòng chứa `--- ` |
| 0.7 | Sửa các chỗ không nhất quán nhỏ: `readOnlyHint` sai cho `play --screenshot`/`pkg.resolve --fetch`; `--size` bị parse hai lần; note "rendering is not simulated"; glob bị chép hai nơi; comment cũ trong `Shims/RecorderShim.cs` | `UaMcp.cs:132-137`, `UaTest.cs:123,146,180`, `UaScene.cs:118` | |

### Giai đoạn 1 · Trải nghiệm agent và hiệu năng lệnh (1–1.5 tuần) · ĐÃ LÀM

> Trạng thái: đã làm cả 9 mục (commit `39b3067` → `8aeaa73`). Kiểm tra cuối: selftest 119 kiểm tra đạt, test của repo 174/174,
> fixture 103/104 (InputTests cần `--input`), đọc–ghi lại 277 file YAML giống hệt, build lại video 110 trùng file đã commit từng
> field (`mu cmp`). Số đo trên repo này (15k `.meta`, 75 package, 149 assembly):
> - 1.1 MCP chạy lệnh đọc / sửa file trong process: `go.tree` 510 → 170 ms, `comp.get` 530 → 175 ms, `asset.refs` 555 → 185 ms,
>   `asset.find --type` 1500 → 490 ms. Không dùng `FileSystemWatcher`: cache được kiểm tra lại theo mtime mỗi lần gọi; lệnh nạp kiểu
>   đã compile (`comp.add`, `so.create`) và `compile` / `test` / `play` / `pkg.*` vẫn chạy trong process con.
> - 1.2 `refindex.json`: `asset.refs` 2.4 → 0.5 s, `asset.missing` 1.3 → 0.6 s (CLI), kết quả giống hệt.
> - 1.3 schema MCP sinh từ dòng usage; 1.4 ảnh trả về dạng image content.
> - 1.5 `--dry-run` / `--diff` trả unified diff; `undo` / `undo.list` (journal 30 lần gần nhất, từ chối khi file đã đổi, `--force`).
> - 1.6 `go.find --where/--layer/--active`, `go.refs`, `comp.move`, `tags.remove`; `scene.save-as` không làm vì `asset.copy` đã đủ.
> - 1.7 stamp theo nội dung + API của assembly phụ thuộc: `touch` 1 file 9 assembly / 7 s → 0 / 1.6 s; sửa thân hàm 9 → 1 assembly.
> - 1.8 `ua.sh` / `mcp.sh` tự build lại khi mã engine mới hơn DLL; 1.9 `Program.Main` thành bảng verb, `mu help`.

| # | Việc | Ghi chú |
|---|---|---|
| 1.1 | **Chế độ daemon cho lệnh đọc/sửa asset**: MCP server giữ `AssetIndex` và cache YAML trong bộ nhớ, làm mới theo mtime hoặc `FileSystemWatcher`. Vẫn tách process con cho `compile`/`play`/`test` (vì không unload được assembly) | Lợi ích lớn nhất: bỏ cold start (host dotnet, JIT, quét mọi `.meta`) cho mỗi lần gọi |
| 1.2 | **Chỉ mục tham chiếu bền** (`Library/MiniUnity/refindex.json`: GUID → danh sách file, cập nhật tăng dần theo mtime) cho `asset.refs`, `asset.missing`, `ref.replace`, `asset.delete` | Hiện tại mỗi lệnh đọc toàn bộ text của project (`AssetDb.cs:128-160`) |
| 1.3 | **Schema MCP riêng cho từng lệnh**, sinh từ chuỗi usage của `Reg(...)`: tham số có tên, kiểu và mô tả | Agent gọi đúng ngay lần đầu, ít phải đọc `help` |
| 1.4 | **Ảnh chụp trả về dạng MCP image content** (`play --screenshot` trả base64 PNG kèm path) | Agent "nhìn" được game mà không phải đọc file |
| 1.5 | **Diff và undo**: `--dry-run` trả unified diff của từng file; mỗi lần ghi lưu bản cũ vào `Library/MiniUnity/undo/<n>/`; thêm lệnh `undo` / `undo.list` | Agent sửa scene an toàn hơn; người review dễ xem |
| 1.6 | **Truy vấn scene mạnh hơn**: `go.find --where "Rigidbody2D.m_Mass>1"`, `--layer`, `--active`; thêm `go.refs` (ai tham chiếu GameObject này); thêm `tags.remove`, `comp.move` (đổi thứ tự component), `scene.save-as` | |
| 1.7 | **Stamp compile theo hash nội dung** (thay cho mtime + độ dài) và cache kết quả `Discover` | `UaCompile.cs:466-485, 528` |
| 1.8 | `ua.sh`/`mcp.sh` phát hiện engine build cũ (so mtime của nguồn `Engine/` với DLL) và tự build lại | Tránh chạy nhầm bản cũ sau khi sửa engine |
| 1.9 | Dọn `Program.Main` (~330 dòng `if args[0]==`): chuyển sang registry giống `Reg(...)`; gom 3 dòng setup lặp ~12 lần; tách các verb debug (`hashbrute`, `hash128test`) ra sau `dev.*` | |

### Giai đoạn 2 · Tách engine khỏi project video và dựng CI (1–1.5 tuần) · ĐÃ LÀM

> Trạng thái (commit `a6f879d` → `6c41ad6`):
> - 2.1–2.3 `Engine/MiniUnity.csproj` build không cần code video; host `Tools/MiniUnityVideo` (`MiniUnity.Video.csproj`, `VideoHost.cs`,
>   `LayoutDump.cs`, script video). Điểm mở rộng: `TypeCache.AddHostAssembly`, `Debug.EchoPrefixes`, `WorldRenderer.RoundedBoxShaders`,
>   `RecordSource`. Script cũ trong `MiniUnity/` chuyển tiếp sang host (xoá khi tách submodule). Video 110 dựng qua host trùng file
>   commit từng field; `record` 2 job, `thumb`, `menu`, `layout` chạy được. Thêm: `comp.add` lấy giá trị mặc định cho script của bất kỳ
>   project nào (compile + nạp như `play`) — trước đây chỉ đúng với script nằm sẵn trong engine.
> - 2.4 `setup.sh --download-unity` / `--check`, SHA-1 cho package, `decompile.sh` báo lỗi ilspycmd; Roslyn và Mono.Cecil từ NuGet.
>   Engine tìm Editor đã tải từ chính thư mục build.
> - 2.5 `.github/workflows/miniunity.yml` (Ubuntu; cache Editor subset / NuGet / package / Library/PackageCache). Đã chạy thử toàn bộ
>   các bước trên một bản clone sạch; lần chạy đầu trên GitHub do push của nhánh này kích hoạt.
> - 2.6 selftest phủ cả 80 lệnh (135 kiểm tra; tìm ra `pkg.search` chưa từng chạy được vì registry Unity không có API search — đã
>   sửa), `tests/run_fixtures.py` (104 fixture, InputTests chạy với `--input`), `tests/roundtrip.py` (277 file YAML).
>   Không chuyển sang pytest: một script tự chứa đủ cho CI và không thêm phụ thuộc.
> - 2.7 `NoWarn` chỉ còn 11 mã thực sự xuất hiện (trước ~60); build engine + host 0 cảnh báo. Chưa bật nullable cho `Agent/`
>   (hàng trăm chú thích, lợi ích thấp so với công sức) — để lại.

| # | Việc | Ghi chú |
|---|---|---|
| 2.1 | (Đã gặp thực tế: thiếu submodule `Assets/Pixel Flow` là `setup.sh` không build được engine.) Tách `MiniUnity.csproj` thành **`MiniUnity.Engine`** (generic) và **`MiniUnity.Host.VideoRecreate`** (compile `Assets/VideoRecreate`, `Pixel Flow`, `Block Exit`, các verb `build`/`produce`/`thumb`/`parts`) | Engine build được không cần code video; điều kiện để làm submodule |
| 2.2 | Thay các chỗ engine biết về video bằng điểm mở rộng: shader tuỳ biến đăng ký qua `ShaderRegistry` (thay cho case `VideoRecreate/RoundedBox` ở `WorldRenderer.cs:36-40`); bộ lọc log theo cấu hình (`EngineMisc.cs:37`); bỏ `using VideoRecreate` trong `LayoutDump.cs` | |
| 2.3 | Chuyển `build.sh`, `produce.sh`, `record.sh`, `partlib.sh`, `parts.sh`, `sheet.sh`, `thumb.sh`, `compare.sh`, `layout.sh` sang `Tools/` của repo video | `MiniUnity/` chỉ còn `setup.sh`, `ua.sh`, `mcp.sh`, `menu.sh`, `validate.sh` |
| 2.4 | **Setup chắc chắn hơn**: checksum SHA-256 cho mọi file tải về; không nuốt stderr của `ilspycmd` và báo lỗi khi file decompile rỗng; tham chiếu Roslyn qua NuGet `Microsoft.CodeAnalysis.CSharp` thay cho `sdk/<ver>/Roslyn/bincore`; `setup.sh --check` để chẩn đoán môi trường | |
| 2.5 | **CI hai tầng** (GitHub Actions): (a) không cần Unity: build engine với stub tối thiểu, chạy các lệnh scene/prefab/asset/YAML của `ua_selftest.py` (`UA_OFFLINE=1`) và kiểm tra round-trip mọi YAML trong repo; (b) runner tự host có Unity Editor: compile, test fixture, play | Tầng (a) cần tách phần selftest không phụ thuộc Editor; tầng (b) phải giữ artefact dẫn xuất từ Unity ở local (theo mục Giấy phép) |
| 2.6 | **Test**: tách `ua_selftest.py` thành pytest theo nhóm lệnh; phủ 22 lệnh chưa có test, `--dry-run`, `batch` (CLI và MCP), `play --screenshot/--call/--audio-out`, đường lỗi (tham số sai, JSON hỏng), MCP parse error; cho `InputTests` chạy mặc định bằng cách tự truyền `--input` | |
| 2.7 | Bỏ bớt ~60 mã warning bị tắt trong csproj, bật lại nullable cho `Engine/Core/Agent` trước | `MiniUnity.csproj:11` |

### Giai đoạn 3 · Hiệu năng runtime và render (1.5–2 tuần) · ĐÃ LÀM (3.2 và một phần 3.5 để lại có lý do)

> Trạng thái (commit `26af817` → `3d6b48c`). Nguyên tắc: tối ưu không được đổi kết quả. Mọi bước được kiểm bằng hash bit-chính-xác:
> `ua bench` trả `digest` (tư thế mọi object, hit raycast, pixel ảnh render) và `record --hash` (SHA-256 pixel mọi frame, không mã
> hoá MP4); hash trước và sau từng thay đổi trùng nhau. Máy đo: 4 vCPU (~2.8 lõi hiệu dụng).
> - 3.6 `ua bench [--only …] [--bodies] [--frames] [--size] [--scene s.unity] [--png]`: physics 3D/2D, 10k raycast, legacy Animation,
>   render 1080p có bóng đổ; ms/frame (mean/p50/p95/max), KB cấp phát/frame, digest. Selftest chạy bench hai lần và so digest (replay).
> - 3.3 Query vật lý không còn `Sync()` toàn cảnh mỗi lần: đếm thay đổi cảnh (`Object.s_SceneEdits`: native set, transform, hierarchy,
>   active/enabled/layer, add/destroy, deserialize); raycast loại nhanh theo AABB, raycast đơn không sort list. **Raycast 742 → 7.6 µs**
>   (300 body). Collider dựng lại hình học theo phiên bản thuộc tính (`m_NVersion`) thay cho so chuỗi; `YScalar` nhớ giá trị đã parse;
>   danh sách object của scene được cache theo `Hier.Version`, `Hier.Walk` không đệ quy. physics3d 11.6 → 9.7 ms, cấp phát 2.6 → 1.7 MB/frame;
>   physics2d 5.0 → 4.7 ms. Chưa làm dynamic AABB tree: broadphase sweep-and-prune hiện tại không còn là phần nặng (Solve ~40%).
> - 3.1 Renderer: visibility buffer cho vật đục (lượt depth giữ tam giác thắng như vẽ tuần tự, rồi shade mỗi pixel một lần, song
>   song theo hàng); shadow cascade raster song song + pool; PCF bớt nửa lần tính CDF; UI raster song song theo dải hàng (mỗi dải đi
>   qua mọi tam giác theo thứ tự nên blend/stencil từng pixel giữ nguyên); `LinearToSrgbByte` vùng tối dùng bảng bậc chính xác (kiểm
>   toàn bộ ~1e9 float, 0 sai khác, cả bản Mono-float). bench render3d **851 → 258 ms/frame**; video (90 frame 1080p, 1 job):
>   12: 97 → 31 s, 20: 54 → 21 s, 21: 60 → 24 s, 110: 58 → 25 s.
> - 3.4 Thứ tự giải contact / kết quả raycast vốn đã tất định (Dictionary giữ thứ tự chèn với cùng chuỗi thao tác); thêm test replay.
> - 3.5 Legacy Animation dùng lại Pose + dictionary đầu ra, bỏ qua object không có Animator/Animation: animation 3.1 → 2.4 ms, cấp phát
>   2.3 → 0.56 MB/frame. Binding `int` cho Animator (Mecanim) chưa làm: chưa có scene đo cho thấy nó nặng.
> - 3.2 Chưa làm, có số đo: mô phỏng ~14 ms/frame so với render ~250 ms/frame (video 110), nên job cuối của video 60 s chỉ mô phỏng
>   lại ~19 s so với ~2 phút render của nó. Snapshot toàn bộ trạng thái engine phức tạp và rủi ro hơn nhiều so với lợi ích đó.


| # | Việc | Kỳ vọng |
|---|---|---|
| 3.1 | Render: chia tile hoặc hàng và rasterize song song (`Parallel.For`); fragment loop chuyên biệt cho từng shader thay cho gọi virtual qua `float[]`; dùng `Vector128/256` cho edge function và blend; tái sử dụng buffer thay cho cấp phát mỗi draw; cache shadow map khi đèn và vật tĩnh không đổi | Từ ~1.5 fps lên 6–10 fps mỗi job ở 1080p trên máy 8 nhân (cần đo) |
| 3.2 | `record --jobs N`: snapshot trạng thái mô phỏng ở các mốc (hoặc một process giả lập rồi chuyển frame cho N worker render) thay cho việc mỗi job mô phỏng lại từ frame 0 | Bỏ chi phí O(N·T) |
| 3.3 | Physics 3D/2D: thay sort LINQ mỗi bước bằng dynamic AABB tree (Box2D `b2DynamicTree`, port sẵn được); BVH cho raycast/overlap; bỏ quét toàn hierarchy trong `Sync()` ở mỗi query (dùng cờ dirty); kiểm tra joint theo cặp bằng `HashSet` thay cho vòng lặp trong `Filter` | Scene vài trăm body chạy tuyến tính |
| 3.4 | Determinism: thay `Dictionary` bằng danh sách có thứ tự chèn ở các chỗ quyết định thứ tự giải contact và kết quả raycast (`B2World.cs:123`, `World.cs:921`); thêm test replay: hai lần chạy cùng input cho cùng hash trạng thái | Replay và quay video ổn định |
| 3.5 | Animator: cache binding theo `int` thay cho key `string`; pool cho Pose; cache `MethodInfo` cho root motion và event | Giảm GC trong `play` dài |
| 3.6 | Benchmark cố định (`ua bench`): physics 500 body, animator 100 nhân vật, render 1080p uGUI + 3D; ghi kết quả vào JSON để so giữa các commit | Có số liệu để không hồi quy |

### Giai đoạn 4 · Độ sát Unity · ĐÃ LÀM (4.9 đánh giá là chưa cần)

Mỗi mục một commit `MiniUnity phase 4.x`, kèm fixture mới trong `tests/fixtures`. Sau mỗi mục: selftest PASS, fixture 100% (136 →
150), `ua test` project 174/174, roundtrip 277/277, hash pixel 90 frame đầu của video 12 / 20 / 21 / 93 / 110 không đổi.

1. **Physics 3D · đã làm:** `excludeLayers` / `includeLayers` của collider và rigidbody, `layerOverridePriority` quyết định khi
   hai bên mâu thuẫn (`World.LayersAllowContact`), dùng cho cả contact lẫn query. Fixture `PhysicsLayerOverrideTests`.
2. **Input System · đã làm:** `Gamepad` và `Joystick` trong backend native (GPAD 28 byte, 'JOY ' 12 byte), chỉ kết nối khi
   `--input` dùng tới; cú pháp `pad.leftStick=(1,0)@10-30`, `pad.buttonSouth@5`, `joy.trigger@…`; legacy `joystick button N` /
   trục joystick. Selftest có phần Input System.
3. **Prefab lồng nhau và variant · đã làm:** `SaveAsPrefabAsset` ghi nested prefab thành `PrefabInstance` (1001) với id stripped
   theo công thức của Unity, lưu instance root thành variant; nạp prefab có PrefabInstance (làm phẳng + nối lại);
   `GetPrefabInstanceHandle`, `IsAnyPrefabInstanceRoot`, `GetCorrespondingObjectFromOriginalSource`, `HasPrefabInstanceAnyOverrides`;
   `AssetDatabase.MoveAsset` / `CopyAsset`. Fixture `PrefabEditorTests` (assembly Editor riêng).
4. **Render 3D · đã làm:** registry shader viết bằng C# (`WorldRenderer.Shaders`): `Unlit/*`, `Sprites/Default`, Standard có albedo
   texture / cutout; hàng đợi Opaque → AlphaTest → Transparent; `MaterialPropertyBlock`; `Graphics.DrawMesh` /
   `RenderMeshInstanced` (màu theo instance); colour space Linear đọc từ ProjectSettings. Host video đăng ký shader của Pixel Flow.
   Fixture `RenderShaderTests`.
5. **Physics 2D · đã làm:** `CompositeCollider2D` hàn outline (`PolygonUnion`: hợp đa giác, lỗ theo chiều kim đồng hồ, chia lồi
   Hertel–Mehlhorn), `GetPath` / `pathCount`, Outlines dùng cạnh có ghost vertex nên vật trượt qua mối nối tile không bị vấp;
   Grid / Tilemap / Tile / `TilemapRenderer` / `TilemapCollider2D` (cả khi gộp vào composite), lưu và nạp tile. Fixture
   `Physics2DCompositeTests`.
6. **Animator · đã làm:** rotation blend theo quaternion cùng bán cầu; layer additive trừ pose tham chiếu (frame đầu hoặc
   `additiveReferencePoseClip` / time); event của mọi clip có weight trong blend tree; `MatchTarget`; `cullingMode` theo
   `Renderer.isVisible` (frustum camera, `GeometryUtility`); API soạn `BlendTree`. Fixture `AnimatorFidelityTests`.
7. **Audio · đã làm:** solo trong mixer; `AudioLowPassFilter` / `HighPass` / `Echo` (có đuôi vang) / `Distortion` và
   `OnAudioFilterRead` (render theo frame trong Play mode, cả nguồn không có clip); filter trên listener áp vào mixdown; Ogg qua
   NVorbis, MP3 qua NLayer (gapless như ffmpeg), ffmpeg chỉ còn là đường dự phòng. Fixture `AudioEffectsTests`.
   Còn thiếu: effect bên trong AudioMixer (send, lowpass của group), reverb / chorus (cho âm đi qua nguyên vẹn).
8. **Playables · đã làm:** `PlayableGraph.Play` được evaluate mỗi frame trong Play mode; mọi playable đang Playing tiến thời gian
   theo speed × speed node cha, quá duration thì `IsDone`; `FrameData` có weight input và effective speed ở cả PrepareFrame lẫn
   ProcessFrame; `AudioClipPlayable` qua `AudioPlayableOutput` phát trên AudioSource đích (volume × weight, pan, seek,
   `AudioMixerPlayable` chuẩn hoá). Recorder tắt phần âm thanh này vì nó tự mix AudioTrack của Timeline. Fixture `PlayableTests`.
   Còn thiếu: `AnimationClipPlayable` / `AnimationMixerPlayable` / `AnimationPlayableOutput` (vẫn là stub).
9. **Humanoid retarget · chưa làm, có chủ đích:** project này không có model FBX, không có avatar hay clip humanoid (3 file
   `.anim` đều generic), nên chưa có gì để kiểm chứng. Làm khi có project cần.
10. **NUnit constraint của Unity · đã làm:** bản NUnit portable được vá bằng Mono.Cecil cho khớp ABI `Constraint.ApplyTo(object)`
    của Unity (`Agent/NUnitAbi.cs`), cộng `Profiler.Recorder("GC.Alloc")` đếm cấp phát; `Mesh.Set*` không còn cấp phát mảng mới
    khi kích thước không đổi (như Unity).
11. **ParticleSystem · đã làm:** `main` / `emission`, `Emit`, `Get/SetParticles`, playback, mô phỏng trong Play mode
    (`Objects/ParticleSystemImpl.cs`). Còn thiếu: shape, các module over-lifetime, noise, collision, sub emitter, trail, vẽ particle.
12. **Play scene game Pixel Flow (`Assets/Pixel Flow/Scenes/Game.unity`) · đã làm:** 184 lỗi → 0 (`MaterialPropertyBlock`,
    UIElements runtime `UIElementsRuntimeUtility` / `Panel`, `RectTransformUtility`, id native của các component còn thiếu);
    scene vẽ được bàn chơi, băng chuyền, xe tăng qua shader C# của host.

### Giai đoạn 5 · Những việc tool chưa làm được: hướng giải quyết · ĐÃ LÀM tuần 1–4 (mục "Sau đó" chờ project cần)

> Trạng thái (commit `4f2124b` → 5.3.1). Đã làm: 5.0.1 `ua apigap` · 5.0.2 oracle test (`tests/run_oracle.py`, chờ số liệu từ
> Unity) · 5.0.3 so ảnh theo vùng (`thumbcmp.sh`, heatmap) · 5.2.2 fake null · 5.2.3 LogAssert · 5.1.1 render callback / `OnRenderImage`
> / `Graphics.Blit` · 5.1.7 particle (Shape, over-lifetime, vẽ billboard) · 5.2.1 IMGUI headless (`ua menu`, `ua window`) · 5.4.2
> animation playables (cả Timeline `AnimationTrack`) · 5.1.2 skybox / ambient từ trời / phản xạ mặc định (ReflectionProbe chưa làm:
> không project nào dùng) · TextMeshPro 3D vẽ bằng shader SDF · 5.1.6 MSAA của camera theo quality level: PSNR thumbnail so với
> Unity 01 39.3 → 57.9 dB, 02 38.9 → 59.4, 13 36.4 → 56.6, 94 39.7 → 57.5 (92 / 93 chụp lúc Editor ở Android, không MSAA: giữ 55.8 / 46.3)
> · 5.2.4 `UnityWebRequest` (mạng tắt mặc định) · 5.4.3 effect trong AudioMixer · 5.4.1 `play --interactive`, viewer web, tool MCP
> `play_*`, `OnMouse*` · 5.3.1 reader SerializedFile nhị phân (`ua serialized.dump`; model lấy kết quả import trong `Library/Artifacts`).
> Kiểm chứng 5.3.1 bằng file thật của Unity (`unity_builtin_extra`): xác nhận file id shader built-in và giá trị Default-Skybox (sửa
> `_SunDisk` thành Simple). Repo không có FBX / `Library/Artifacts` nên đường mesh từ FBX chưa được thử với artifact thật; texture nén
> (DXT / BC7 / ASTC) chưa giải mã (MiniUnity đọc PNG nguồn). Hash 5 video hiện tại: 12 `bb52144b411e217b`, 20 `eea25ec93e18c2ba`,
> 21 `90d45cd25bf08069`, 93 `9fd242b51c3a9fa6`, 110 `ef684f6576456919`.

Phạm vi: mọi mục "Không làm được" trong README / AGENT.md và phần "còn thiếu" của Giai đoạn 3–4, **trừ build player (APK / IPA /
exe)** — tạm bỏ qua theo yêu cầu. Mỗi mục ghi: hiện trạng và vì sao chưa làm được, các hướng, hướng chọn, công, và cách nghiệm thu.
Số liệu đo trên repo này (2026-10-15): stub `NotImplementedException` còn lại theo module: UnityEditor.Core 6842, UnityEngine.Core 4504
(trong đó `CommandBuffer` 331, `Camera` 120, `Graphics` 98, `Shader` 94, `Material` 90, `QualitySettings` 122), UIElements 1500,
IMGUI 816 (+ `EditorGUI` 301, `EditorGUILayout` 287), ParticleSystem 532, Terrain 303, UnityWebRequest 161, Animation 364, Audio 120.
Code project dùng: `ParticleSystem` 20 chỗ (prefab `Debris` của Pixel Flow: chỉ Initial + Shape bật), `UnityWebRequest` 5 (tool TTS
trong Editor), `EditorWindow` + `OnGUI` 2 cửa sổ (`LevelBakerWindow`, `TTSWindow`), `CustomEditor` 2, Cinemachine 8 file, LitMotion
42, không có FBX, không có lightmap đã bake, không có UIDocument, Terrain, VideoPlayer, NavMesh, networking runtime.

#### 5.0 · Làm cho "chưa làm được" đo được (làm trước, 1–2 ngày)

| # | Việc | Hướng | Nghiệm thu |
|---|---|---|---|
| 5.0.1 | **`ua apigap`** — báo cáo API còn thiếu theo project | Hai nguồn: (a) **tĩnh**: `ua compile` đã có Roslyn semantic model → liệt kê mọi member `UnityEngine.*` / `UnityEditor.*` mà code project gọi và đang là stub ném `NotImplementedException` (đọc danh sách stub từ `Engine/Generated` lúc gen, ghi ra `stubs.json`); (b) **động**: `play` / `test` / `record` gom mọi exception có nhãn `"miniunity"` (đã có) thành bảng `Type.Member → số lần, file:line gọi`. Xuất JSON + markdown, xếp theo số lần dùng | Chạy trên Pixel Flow + Block Exit + VideoRecreate ra danh sách xếp hạng; selftest kiểm tra lệnh |
| 5.0.2 | **Oracle test**: bộ test chạy được trên cả Unity thật lẫn MiniUnity, kết quả Unity commit vào repo | Thư mục `tests/oracle/*.cs` ([Test] ghi số liệu ra JSON bằng `TestContext`); người dùng chạy một lần trong Unity (`Window ▸ Test Runner` hoặc `-runTests -batchmode`) và commit `tests/oracle/expected/*.json`; `ua test --oracle` so sánh với sai số cho phép. Dùng cho mọi câu hỏi "Unity làm thế nào?" còn mở: dấu motor `HingeJoint2D`, blend quaternion, rolloff audio, thứ tự callback render… | Case đầu tiên: motor `HingeJoint2D` (AGENT.md ghi "chưa đối chiếu được") |
| 5.0.3 | **So ảnh với Unity theo vùng** | `Exports/<video>/` có MP4 Unity dựng; thêm `compare.sh --frames`: tách frame bằng ffmpeg, PSNR / SSIM theo lưới 16×9, xuất heatmap PNG + top vùng lệch nhất kèm object chiếm vùng đó (từ visibility buffer). Biến con số "35–43 dB" thành danh sách nguyên nhân cụ thể | Heatmap cho video 12, 20, 21, 93, 110 |

#### 5.1 · Render

| # | Hạn chế | Vì sao chưa làm được | Hướng giải quyết | Công · nghiệm thu |
|---|---|---|---|---|
| 5.1.1 | **Render trong player loop**: `OnPreCull` / `OnPreRender` / `OnPostRender` / `OnRenderImage` / `OnWillRenderObject` / `OnBecameVisible` / `OnBecameInvisible` / `OnRenderObject` không được gọi; `Camera.Render()` chỉ chạy khi chụp ảnh / quay video | Raster hoá mỗi frame tốn ~250 ms/frame, nên `play` không render; vì thế không có pha nào để gọi callback | **Tách "cull" khỏi "raster"**: thêm pha render vào `Play.Step` (sau LateUpdate, như Unity) chỉ làm phần rẻ: với mỗi camera bật, tính frustum và tập renderer nhìn thấy (`Visibility.cs` đã có, 4.6), gọi `OnPreCull` → `OnBecameVisible/Invisible` (diff tập nhìn thấy với frame trước) → `OnPreRender` → `OnWillRenderObject` → `OnPostRender`. Raster chỉ khi có người cần (screenshot / record / `RenderTexture` đích có script đọc). `OnRenderImage(src, dst)`: khi đang record thì chạy thật với RT của frame (host có thể cắm image effect viết bằng C#); khi không record thì gọi với RT rỗng cùng kích thước để script vẫn chạy được (cờ `--no-render-callbacks` để tắt). `Renderer.isVisible` chuyển sang đọc kết quả cull của frame (không tính lại) | 1–2 ngày · fixture đếm callback theo thứ tự Unity; hash video 5 clip không đổi |
| 5.1.2 | **Skybox, ambient từ sky, reflection probe** | 26/27 scene dùng `Default-Skybox` (Skybox/Procedural) nhưng renderer chỉ clear màu; Standard không có phản xạ môi trường nên vật kim loại / bóng tối sai nhiều so với Unity | (1) Port shader `Skybox/Procedural` (Preetham, ~80 dòng) và `Skybox/6 Sided` / `Cubemap` vào registry C#: vẽ ở pass clear khi `clearFlags = Skybox`. (2) Ambient `Skybox` mode: chiếu sky ra SH bậc 2 (lấy mẫu 64 hướng) như Unity; `RenderSettings.ambientProbe`. (3) Phản xạ: Standard lấy specular môi trường từ cubemap mờ theo roughness; nguồn mặc định là sky (Unity làm vậy khi không có probe); `ReflectionProbe` realtime = render 6 mặt 64–128 px bằng chính renderer, baked = đọc texture probe nếu có (cần 5.3.1 cho `.exr`) | 3 ngày · so vùng nền trời và vật kim loại với heatmap 5.0.3; fixture PSNR của quả cầu metallic ≥ 30 dB so với ảnh Unity (oracle) |
| 5.1.3 | **Lightmap, light probe** | Không có dữ liệu bake trong repo (0 `LightingData.asset`), texture lightmap là `.exr` chưa đọc được | Chỉ làm khi có project bake: đọc `Lightmap-*_light.exr` (OpenEXR scanline, nén PIZ/ZIP: viết reader ~600 dòng hoặc gói NuGet), `LightingData.asset` là binary → đọc qua 5.3.1; shader Standard lấy `unity_Lightmap` theo `lightmapScaleOffset` của renderer; light probe SH từ `LightProbes.bakedProbes`. Hiện tại: ghi rõ trong `ua apigap` là "scene có bake nhưng MiniUnity bỏ qua" | 2–3 ngày khi cần · fixture với scene bake nhỏ |
| 5.1.4 | **Post-processing** | Không có stack trong manifest (Built-in RP, không PPv2) → chưa cần | Sau 5.1.1, host đăng ký image effect C# (bloom, vignette, LUT, tone map) chạy ở `OnRenderImage`; nếu sau này dùng PPv2 / URP Volume thì đọc `VolumeProfile` và map sang các effect đó | Khi có project cần |
| 5.1.5 | **Shader tuỳ biến** (`.shader` ShaderLab / Shader Graph) | Không có trình dịch ShaderLab → chỉ chạy shader đã port tay (registry) | Không dịch HLSL chung (quá đắt, lợi ích thấp với 3 `Shader.Find` trong repo). Thay vào đó: (1) **fallback theo property**: shader lạ có `_MainTex` / `_Color` / `_Cutoff` / `_BumpMap` → chạy như Unlit-texture / Standard tương ứng, ghi log một lần; (2) `ua shaders`: liệt kê material → shader → cài đặt đang dùng (thật / fallback / không vẽ); (3) Shader Graph: đọc `.shadergraph` JSON, hỗ trợ tập node cơ bản (Sample Texture 2D, Multiply, Add, Lerp, Fresnel, Time, UV, Color) dịch sang cây biểu thức C# chạy trong registry — làm khi project có graph | 1 ngày (1, 2); 4–5 ngày (3) khi cần |
| 5.1.6 | **Sai khác ảnh 35–43 dB so với Unity** | Chưa biết nguyên nhân chính vì chưa đo theo vùng | Dùng 5.0.3 để xếp hạng; dự đoán theo thứ tự: MSAA (`QualitySettings.antiAliasing` 2/4/8 → raster coverage 4 mẫu/pixel chỉ ở cạnh tam giác), mipmap + trilinear / anisotropic khi lấy mẫu texture, SDF text của TMP (outline, underlay, softness), PCF bóng; mỗi mục một commit, đo lại PSNR | 3–5 ngày · PSNR trung bình 5 video tăng ≥ 3 dB |
| 5.1.7 | **Vẽ particle; các module còn lại** | `ParticleSystemRenderer` chưa vẽ; module shape / over-lifetime / noise / collision / sub emitter / trail chưa mô phỏng. Prefab `Debris` (Pixel Flow) bật Initial + Shape → video Pixel Flow hiện **thiếu mảnh vỡ** | Vẽ: billboard / stretched / horizontal / mesh trong `WorldRenderer` với shader Sprites/Default hoặc Particles/Standard Unlit (additive, alpha), sort theo `sortingFudge` / khoảng cách, queue Transparent. Mô phỏng theo thứ tự dùng: Shape (sphere, hemisphere, cone, box, circle, edge, mesh vertex), Size / Color / Rotation / Velocity / Limit Velocity over lifetime (gradient + curve đã có trong `ClipData`), Noise (simplex 3D), Collision (plane; world qua `Physics.Raycast` đã có), Sub emitter (birth / death / collision), Trail (dải như LineRenderer). Xác thực bằng oracle test 5.0.2: Unity ghi vị trí particle sau N frame với seed cố định (`useAutoRandomSeed = false`) | 4 ngày · video 93 (Pixel Flow) có mảnh vỡ; fixture `ParticleRenderTests` |
| 5.1.8 | `LineRenderer`, `TrailRenderer`, `SkinnedMeshRenderer`, `TextMesh` không vẽ | Chưa scene nào dùng (0 trong repo) | LineRenderer / TrailRenderer: dải quad hướng camera, `widthCurve`, `colorGradient`, corner / cap vertices (1 ngày). SkinnedMeshRenderer: skinning CPU theo `bones` + `bindposes` (0,5 ngày, cần mesh có skin → 5.3.1). TextMesh: dùng font bitmap như uGUI Text | Khi scene dùng |

#### 5.2 · Editor

| # | Hạn chế | Vì sao | Hướng giải quyết | Công · nghiệm thu |
|---|---|---|---|---|
| 5.2.1 | **`EditorWindow` + IMGUI (`OnGUI`, `GUILayout`, `EditorGUILayout`, `CustomEditor`, `PropertyDrawer`)** — tool riêng của project (`LevelBakerWindow` bake level từ texture, `TTSWindow`) không chạy được | 816 + 588 stub; IMGUI là immediate mode: mỗi sự kiện chạy lại `OnGUI`, control nhận diện bằng vị trí gọi | **IMGUI headless**: cài `GUI` / `GUILayout` / `EditorGUI` / `EditorGUILayout` dạng "ghi lại cây control": mỗi lượt `OnGUI` với `Event.current = Layout` rồi `Repaint` ghi danh sách control (loại, label, giá trị hiện tại, id theo thứ tự gọi + label). Lệnh `ua window <Type> [--set "Ammo Per Tank=3"] [--click "Bake"] [--dump]`: `GetWindow`, chạy `OnGUI` lượt 1 để lấy cây, `--set` gán giá trị cho control khớp label (`IntField`, `FloatField`, `TextField`, `Toggle`, `Popup`, `Slider`, `ObjectField` nhận đường dẫn asset), `--click` làm `GUILayout.Button` khớp label trả `true` ở lượt tiếp; MCP tool tương ứng. `ua inspector <obj>` chạy `CustomEditor.OnInspectorGUI` qua cùng bộ máy (`SerializedObject` / `SerializedProperty` đã có). `Handles`, `Gizmos`, `EditorGUIUtility` vẽ: no-op. Phủ ~40 control hay dùng trước; control chưa có ném `NotImplemented` như thường lệ | 3–5 ngày · chạy được `LevelBakerWindow` bake một level và ra cùng asset như Unity (so YAML) |
| 5.2.2 | **`UnassignedReferenceException`**: field Unity chưa gán đang là `null` thật (`NullReferenceException`) | MiniUnity không có "fake null" của Unity | Khi deserialize `{fileID: 0}` vào field kiểu `UnityEngine.Object`, gán **sentinel** theo kiểu khai báo (`RuntimeHelpers.GetUninitializedObject`, `m_Destroyed = true`, `m_Unassigned = (fieldName, ownerType)`); `==`, `bool`, `?:` đã coi object destroyed là null (như Unity); mọi truy cập member qua `NGet`/`transform`/`name` ném `UnassignedReferenceException` đúng thông điệp Unity ("The variable X of Y has not been assigned…"). `MissingReferenceException` cho object đã `Destroy` (hiện ném gì? kiểm tra). Bật qua `MU_FAKE_NULL=1` một phiên bản rồi thành mặc định; khác biệt duy nhất còn lại là `?.` / `is null` — Unity cũng vậy | 1–2 ngày · fixture: field chưa gán → `UnassignedReferenceException`, `== null` vẫn `true` |
| 5.2.3 | **`LogAssert`**: log Error / Exception không được `LogAssert.Expect` không làm test fail | Chỉ thu log, chưa áp luật "Unhandled log message" của Test Framework | Sau mỗi test, hỏi `LogScope` của Test Framework các log lỗi chưa được expect → đánh dấu test Failed với message `Unhandled log message: [Error] …` (y hệt Unity); `LogAssert.ignoreFailingMessages` tôn trọng; cờ `--no-log-fail` để tắt | 0,5 ngày · fixture: test log `Debug.LogError` không expect → Failed; có `Expect` → Passed |
| 5.2.4 | **Code Editor gọi mạng** (`TTSProviders` dùng `UnityWebRequest`) | 161 stub; chưa có networking | `UnityWebRequest` trên `HttpClient`: `Get/Post/Put/Head/Delete`, header, `uploadHandler` / `downloadHandler` (Buffer, File, Texture, AudioClip, Script), `result` / `responseCode` / `error` / `timeout`, `SendWebRequest` trả `AsyncOperation` hoàn tất trên player loop (trong lệnh Editor không có loop: chạy đồng bộ khi `isDone` / `yield` được hỏi). **Mặc định tắt**: trả `ConnectionError` "network disabled by MiniUnity" trừ khi `MU_NETWORK=1` hoặc `--allow-network host1,host2` — agent không được tự gọi ra ngoài | 1–2 ngày · fixture với server HTTP nội bộ (`HttpListener`) |

#### 5.3 · Import và dữ liệu Unity đã xử lý

| # | Hạn chế | Vì sao | Hướng giải quyết | Công · nghiệm thu |
|---|---|---|---|---|
| 5.3.1 | **Đọc kết quả import của Unity** (`Library/`): mesh / avatar / clip humanoid từ FBX, `LightingData.asset`, lightmap, texture đã nén, audio đã import | MiniUnity chỉ đọc YAML text; FBX, EXR, binary serialized file chưa có reader | **Đòn bẩy chung**: reader **SerializedFile** (định dạng binary của Unity, phiên bản 22, cùng cấu trúc với YAML: TypeTree + object) đọc `Library/Artifacts/**` và `Library/metadata` (ánh xạ GUID → artifact qua `ArtifactDB`/SQLite… hoặc đơn giản hơn: đọc `Library/ArtifactDB` bằng thư viện nhỏ; phương án B: `Library/Bee`/`ScriptAssemblies` không đủ). Dùng gói NuGet **AssetsTools.NET** (MIT, .NET 8) thay vì viết mới; trích `Mesh` (vertices, normals, uv, bone weights, bindposes), `Avatar` (HumanDescription), `AnimationClip` humanoid (muscle curves), `Texture2D` (giải nén DXT/ETC/ASTC → RGBA; BC1–7 ~400 dòng), `LightmapSettings`/`LightingData`. Điều kiện: project đã mở trong Unity một lần (giống yêu cầu `Library/PackageCache` hiện nay). Khi `Library` thiếu artifact: fallback như hiện tại (mesh rỗng + log `ua apigap`) | 3–5 ngày · fixture: FBX nhỏ commit kèm Library artifact mẫu → mesh đúng số đỉnh, bounds như Unity |
| 5.3.2 | **Humanoid retarget** (muscle space) | Không có FBX / avatar trong repo; không có dữ liệu để kiểm | Sau 5.3.1: đọc `HumanDescription` + skeleton; cài **muscle → quaternion** theo công thức Unity (mỗi DoF là xoay quanh trục cục bộ với min/max từ `HumanLimit`, sign & axes theo bảng `HumanTrait.MuscleName`); clip humanoid gồm curve muscle + root `T`/`Q` → pose; `Animator.humanScale`, `bodyPosition/Rotation`, IK đã có bám theo. Kiểm bằng oracle 5.0.2: Unity xuất vị trí xương sau khi `SampleAnimation` tại vài thời điểm | 4–5 ngày khi có project cần · sai lệch vị trí xương < 1 mm trên model mẫu |
| 5.3.3 | Đọc FBX trực tiếp (không cần `Library`) | Định dạng binary phức tạp | Chỉ khi 5.3.1 không đủ (CI không có `Library`): port `ufbx` (C, public domain, một file) sang C# hoặc gọi qua P/Invoke bản native build sẵn cho Linux/macOS/Windows; lấy mesh, skin, animation, không cần material | 5+ ngày · để sau |

#### 5.4 · Runtime

| # | Hạn chế | Vì sao | Hướng giải quyết | Công · nghiệm thu |
|---|---|---|---|---|
| 5.4.1 | **Input thiết bị thật** | Headless; chỉ có `--input` kịch bản | Không cần thiết bị: cần **vòng tương tác**. `play --interactive`: tiến trình `play` giữ lại, MCP tool `play.input` đẩy sự kiện (phím, chuột, cảm ứng, gamepad — cùng cú pháp `--input`), `play.step N`, `play.screenshot` trả PNG; viewer web nhỏ (một file HTML, kết nối WebSocket của daemon) hiển thị ảnh và chuyển phím / chuột của người dùng về. Agent và người đều "chơi" được game trong tool, không cần Unity | 2–3 ngày · selftest: gửi `mouse0` qua MCP, script `OnMouseDown` thấy |
| 5.4.2 | **Animation playables** (`AnimationClipPlayable`, `AnimationMixerPlayable`, `AnimationLayerMixerPlayable`, `AnimationPlayableOutput`, Timeline `AnimationTrack`) | Còn là stub (4.8 chỉ làm audio); repo không dùng `AnimationTrack` nên chưa lộ | `AnimationClipPlayable` lấy mẫu `ClipData` tại thời gian node vào `Pose`; mixer trộn pose theo weight (dùng `Pose.Add` + `AlignRotationsTo` của 4.6); layer mixer như layer của Animator (override / additive, mask); `AnimationPlayableOutput` ghi pose lên `PoseWriter` của Animator đích sau khi Animator chạy (Unity: graph nối vào Animator thay controller); Timeline `AnimationTrack` (clip, offset, extrapolation, track offset) đi qua đó | 2 ngày · fixture: graph 2 clip trộn 50/50 cho vị trí trung bình; Timeline AnimationTrack chạy trong `record` |
| 5.4.3 | **Effect trong AudioMixer** (send / receive, lowpass / highpass / echo / reverb / compressor / duck volume của group, pitch shifter) | Mixer chỉ nhân volume; voice được trộn offline từ snapshot gain, không có bus theo group | Chuyển `MixDown` và `OutputData` sang **mixer theo khối**: mỗi frame render mọi voice (như đường "live" của 4.7) vào bus của group → chuỗi effect của group (tái dùng `FilterStage`; reverb = port Freeverb, public domain; compressor / duck = envelope follower; send → bus đích theo level) → group cha → master. Tham số effect đọc từ `m_Effects` + giá trị theo GUID trong snapshot (đã có `ParamValue`). Giữ hash âm thanh của video không đổi khi mixer không có effect (so WAV bit-exact) | 2–3 ngày · fixture: lowpass trên group làm 440 Hz tắt; send sang group reverb có đuôi |
| 5.4.4 | `ArticulationBody` jacobian / inverse dynamics (`GetDenseJacobian`, `GetJointGravityForces`, `GetJointCoriolisCentrifugalForces`, `GetDriveForces`) | Chưa có thuật toán; project không dùng | Jacobian: cột i = vận tốc không gian của mọi link khi `qdot = e_i` (dùng forward kinematics sẵn có; chính xác vì tuyến tính). Gravity / Coriolis: RNEA (Recursive Newton–Euler) với `qdd = 0`, lần lượt `qd = 0` và `qd` thật, trừ nhau. Thứ tự DoF theo `GetDofStartIndices` | 2–3 ngày khi cần · fixture pendulum 2 khâu so với công thức giải tích |
| 5.4.5 | Dấu motor `HingeJoint2D`, các nghi vấn độ sát khác | Không có Unity để đối chiếu | Oracle test 5.0.2 | — |
| 5.4.6 | Kiểm Cinemachine (8 file dùng) và LitMotion (42) chạy đúng trong `play` | Engine có vá riêng cho Cinemachine (Undo, script registry) nhưng chưa có fixture | Fixture: vcam follow + `CinemachineBrain` blend 2 vcam, so vị trí camera sau N frame với oracle Unity; LitMotion: tween `Bind` 1 s | 1 ngày |

#### 5.5 · Không làm, có chủ đích

| Việc | Lý do | Nếu một ngày cần |
|---|---|---|
| Build player (APK / IPA / exe) | Theo yêu cầu | — |
| UI Toolkit runtime (UIElements, 1500 stub) | Repo không có `UIDocument` | Phần managed của UIElements decompile được như uGUI (`managed_types.txt`); cần backend vẽ (UIR) → nối vào `UIRenderer` (mesh + texture + clip), ~1 tuần |
| Terrain (303 stub), VFX Graph, Cloth, Video Player, NavMesh (AI), XR | Không dùng trong repo | NavMesh: port Recast/Detour (lớn); Terrain: heightmap mesh + splat vào Standard (3 ngày); VideoPlayer: giải mã qua ffmpeg thành texture |
| Networking runtime (Netcode, Transport) | Không dùng | — |
| Dịch ShaderLab / HLSL tổng quát | Đắt, lợi ích thấp | 5.1.5 (3) cho Shader Graph |

#### Thứ tự đề xuất

```
Tuần 1   5.0.1 apigap · 5.0.2 oracle (motor HingeJoint2D) · 5.0.3 heatmap · 5.2.3 LogAssert · 5.2.2 fake null
Tuần 2   5.1.1 render callbacks · 5.1.7 particle (vẽ + Shape; Pixel Flow có mảnh vỡ) · 5.2.1 IMGUI headless (LevelBakerWindow)
Tuần 3   5.1.2 skybox / ambient / reflection · 5.1.6 theo heatmap · 5.4.2 animation playables
Tuần 4   5.3.1 SerializedFile reader · 5.4.3 mixer effect · 5.2.4 UnityWebRequest (tắt mặc định) · 5.4.1 play --interactive
Sau đó   5.3.2 humanoid · 5.1.3 lightmap · 5.4.4 articulation · 5.1.8 các renderer còn lại — khi có project cần
```

Nguyên tắc giữ nguyên từ Giai đoạn 3–4: mỗi mục một commit kèm fixture; selftest / fixture / `ua test` / roundtrip xanh; hash pixel
của 5 video không đổi trừ khi mục đó cố ý thay đổi ảnh (5.1.2, 5.1.6, 5.1.7) — khi đó ghi hash mới kèm ảnh so sánh trong commit.

## 4. Thứ tự đề xuất và mốc kiểm tra

```
Tuần 1      Giai đoạn 0 (an toàn) ───► phát hành nội bộ
Tuần 2–3    Giai đoạn 1 (daemon, refindex, schema MCP, ảnh, diff/undo)
Tuần 3–4    Giai đoạn 2 (tách engine, CI tầng a, test)
Tuần 5–6    Giai đoạn 3 (render song song, broadphase, determinism, bench)
Sau đó      Giai đoạn 4 theo nhu cầu từng game
```

| Chỉ số | Hiện tại | Mục tiêu |
|---|---|---|
| Lệnh đọc qua MCP (`go.tree`, `comp.get`) | ~510 ms (cold start mỗi lần) | < 200 ms · **đạt: ~170 ms** |
| `record` 1080p | ~1.5 fps/job | ≥ 6 fps/job · **video 110: 1.55 → 3.6 fps/job trên 4 vCPU** (song song trong job; nhiều lõi hơn → nhanh hơn) |
| Lệnh `ua` có test | ~53/75 | 75/75, kèm đường lỗi |
| CI | Không có | Tầng (a) chạy trên mỗi PR |
| `catch {}` trống trong Serializer/Agent | 13 | 0 (Serializer: 0 sau Giai đoạn 0) |
| Engine build không cần code video | Không | Có |

## 5. Việc nên làm ngay

~~Năm việc nhỏ, rủi ro thấp, lợi ích rõ, có thể làm trong một PR: **0.1** (SafeIO), **0.2** (ghi nguyên tử), **0.3** (timeout MCP),
**0.4** (batch luôn trả JSON) và **Giai đoạn 4 mục 1** (layer filter cho collider 3D).~~ Đã làm cùng các giai đoạn 0–4.

Phần còn để ngỏ: retarget humanoid (4.9), Animation playables, effect trong AudioMixer, các module ParticleSystem còn lại, vẽ
particle; 3.2 và phần còn lại của 3.5 (lý do ghi ở Giai đoạn 3).

## 6. Bổ sung sau giai đoạn 5 · ĐÃ LÀM

| Việc | Kết quả |
|---|---|
| Tách khỏi repo video | `config/defaults.cfg` (phiên bản Unity và package khi không có project), `setup.sh --unity-version / --unity-revision`, fixture mang ProjectSettings và asset riêng (`tests/data`), mọi script có quyền thực thi, CI job `standalone` chạy MiniUnity một mình |
| `Text` cũ của uGUI | `Font` động (atlas Alpha8 tự lớn, `Font.textureRebuilt`), `TextGenerator` (rich text, wrap, truncate, best fit, căn lề, line spacing); font builtin dùng Liberation Sans (cùng số đo Arial) |
| `SceneManager.LoadScene` lúc chạy | theo tên / đường dẫn / build index, single và additive, async (`allowSceneActivation`), `UnloadSceneAsync`, sự kiện `sceneLoaded` / `sceneUnloaded` / `activeSceneChanged`, `DontDestroyOnLoad`; player loop, Find*, vật lý, render thấy mọi scene đã nạp |
| Render pipeline, PostProcessing | `GraphicsSettings.currentRenderPipeline` (null với built-in), `GetShaderMode`, quality level; nhánh `UNITY_EDITOR` khi Play chạy được (`TypeCache`, `StageUtility`); PostProcessing v2 chạy trong Play (command buffer được ghi nhận nhưng không thực thi); asset trong package resolve theo `Packages/<tên>/…` |
| FBX không cần `Library/` | đọc FBX binary / ASCII → hierarchy, mesh, material, texture; quy đổi trục, đơn vị, x lật như ModelImporter; chưa có skin / animation / camera / light |
| Texture albedo trong shader 3D | giải mã JPEG (baseline / progressive), TGA, PSD; `_BaseMap` / `_BaseColor` (URP và shader chưa port vẽ như Standard); `ImageConversion.LoadImage` |
