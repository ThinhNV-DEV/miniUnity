# MiniUnity Agent · thao tác project Unity không cần mở Unity

`mu ua` là môi trường để agent (hoặc script, CI) đọc và sửa một project Unity 2022.3, compile C#, chạy test và Play scene
mà không cần Unity Editor chạy hay license. Mỗi lệnh in ra **một object JSON** (`ok`, `error`, kết quả, danh sách file đã ghi
trong `written`). Ngoài CLI còn có **MCP server**: mỗi lệnh là một tool.

```bash
./ua.sh help                                            # mọi lệnh + cú pháp
./ua.sh go.tree Assets/Scenes/Main.unity --depth 2      # project mặc định: repo chứa MiniUnity
UNITY_PROJECT=/path/to/Game ./ua.sh compile             # project khác
./ua.sh batch edits.jsonl [--dry-run] [--stop-on-error] # nhiều lệnh, mỗi dòng: ["go.create", "Assets/Main.unity", "Player"]
./mcp.sh /path/to/Game                                  # MCP server (stdio)
```

Cần: `./setup.sh` đã chạy (stub API + build) và Unity Editor 2022.3 đã cài, chỉ để đọc reference assembly và source uGUI.
Các lệnh sửa scene/asset hoạt động cả khi không có Editor; `compile`, `test`, `play` thì cần Editor.

## Quy ước

- **Đường dẫn** tính từ gốc project (`Assets/…`, `Packages/…`). Asset có thể chỉ định bằng path hoặc GUID.
- **GameObject** trong scene/prefab được chỉ định bằng đường dẫn Hierarchy: `Canvas/Panel/Button`, `Item[1]` (trùng tên thì lấy
  thứ hai), `#1234567` (fileID). Object bên trong prefab instance cũng có đường dẫn như object thường.
- **Giá trị** truyền dạng JSON: `5`, `"text"`, `{"x":1,"y":2,"z":0}`, `[1,2]`. Với tham chiếu object:
  `{"$ref":"Assets/Art/hero.png"}` (main object), `{"$ref":"Assets/Art/atlas.png","$sub":"icon_3"}` (sub-asset),
  `{"$ref":"go:Player/Weapon","$component":"Transform"}` (object trong cùng file), `{"$ref":"#123"}`, hoặc
  `{"fileID":…,"guid":…,"type":…}` thô. Field kiểu tham chiếu nhận luôn chuỗi path.
- **Property path** như `SerializedProperty`: `m_LocalPosition.x`, `items.Array.data[2].name`, `items.Array.size`, viết tắt `items[2]`.
- `--dry-run`: chạy đầy đủ nhưng không ghi gì; kết quả liệt kê `written` và `diff` (unified diff của từng file text, file nhị phân
  chỉ ghi kích thước, file bị xoá / di chuyển). `--diff` trả `diff` cả khi ghi thật.
- **Undo**: mỗi lệnh ghi lưu trạng thái cũ vào `Library/MiniUnity/undo/<n>/` (giữ 30 lần gần nhất) và trả `"undo": n`. `undo` hoàn tác
  lệnh ghi gần nhất (khôi phục file, xoá file / thư mục lệnh đã tạo, chuyển ngược file đã di chuyển); từ chối nếu các file đó đã bị
  sửa sau lệnh, trừ khi có `--force`. `undo.list` liệt kê những gì hoàn tác được. Lệnh lỗi giữa chừng vẫn có journal.
- `batch` luôn in một object JSON: dòng JSONL hỏng thành kết quả lỗi kèm `line`, các dòng sau vẫn chạy (trừ khi có
  `--stop-on-error`: phần còn lại được đếm trong `skipped`). `--dry-run` của `batch` áp dụng cho mọi dòng.
- Giá trị không đọc / ghi được khi serialize (getter hoặc `OnBeforeSerialize` / `OnAfterDeserialize` ném lỗi, kiểu không tạo được)
  vẫn được ghi bằng giá trị mặc định như trước, nhưng được liệt kê trong `warnings`; thêm `--strict` để lệnh thất bại thay vì ghi.
- Ghi file an toàn: chỉ trong thư mục project (không qua symlink), ghi ra file tạm rồi đổi tên, nên process bị dừng giữa chừng
  không để lại scene / asset bị cắt cụt.
- File YAML được ghi lại **lossless**: phần không sửa giữ nguyên từng byte (mọi file YAML trong repo đọc–ghi lại giống hệt).

## Lệnh

| Nhóm | Lệnh | Ghi chú |
|---|---|---|
| Scene | `scene.list` · `scene.new <path> [--basic]` · `buildscenes.get/set` | `--basic` = Main Camera + Directional Light |
| Hierarchy / GameObject | `go.tree` · `go.find` · `go.get` · `go.refs` · `go.create` · `go.delete` · `go.rename` · `go.move` · `go.duplicate` · `go.set` | `go.create --components BoxCollider,Rigidbody --position {…} [--rect]`; `go.set --active/--layer/--tag/--static`; `go.find --name/--component/--tag/--layer/--active` và `--where "Rigidbody2D.m_Mass>1; Image.m_Sprite==null"` (so sánh giá trị serialized: `==` `!=` `>` `>=` `<` `<=` `~=`); `go.refs`: field nào trong file tham chiếu tới object / component của nó |
| Component / field | `comp.list` · `comp.get` · `comp.set` · `comp.add` · `comp.remove` · `comp.move` | `comp.add` theo tên class, tên script, đường dẫn `.cs` hoặc GUID; field mặc định lấy từ type đã compile (script của project:
compile tăng dần rồi nạp như `play`, cần Unity Editor; project lỗi compile thì thêm component không kèm giá trị mặc định và có `warnings`); tôn trọng `[RequireComponent]` |
| Prefab | `prefab.instantiate` · `prefab.overrides` · `prefab.revert` · `prefab.create` · `prefab.unpack` | Sửa object trong instance sẽ thành override (`m_Modifications`, added/removed component và GameObject), giống Unity |
| ScriptableObject / YAML | `so.create` · `obj.list` · `obj.get` · `obj.set` | `obj.*` đọc/sửa document bất kỳ của file YAML (`.asset`, `.mat`, `.anim`, …) |
| Asset / GUID / reference | `asset.find` · `asset.info` · `asset.refs` · `asset.deps` · `asset.missing` · `asset.move` · `asset.copy` · `asset.delete` · `asset.mkdir` · `asset.import` · `asset.labels` · `guid.of` · `guid.path` · `guid.regenerate` · `ref.replace` | `asset.move` giữ GUID (tham chiếu không vỡ); `asset.delete` từ chối khi còn tham chiếu (trừ `--force`); `asset.import` tạo `.meta` còn thiếu; tra tham chiếu dùng chỉ mục `Library/MiniUnity/refindex.json` (chỉ đọc lại file đã đổi) |
| Import / settings | `meta.get` · `meta.set` · `settings.list` · `settings.get` · `settings.set` · `tags.add` · `tags.remove` | `meta.set Assets/a.png TextureImporter.maxTextureSize 1024`; `settings.set PlayerSettings productName '"Sand Loop"'` |
| Package | `pkg.list` · `pkg.info` · `pkg.search` · `pkg.add` · `pkg.remove` · `pkg.deps` · `pkg.resolve [--fetch] [--write-lock]` | Resolve như Unity Package Manager (dependency trực tiếp thắng, sau đó version cao nhất); `--fetch` tải source về `Library/PackageCache` |
| Addressables | `addr.info` · `addr.init` · `addr.group.*` · `addr.entries` · `addr.entry.add/remove/set` · `addr.label.add/remove` · `addr.schema.get/set` · `addr.analyze` | `addr.analyze`: dependency ngầm bị nhân đôi giữa group, entry thiếu, scene vừa addressable vừa ở Build Settings, address trùng |
| Compile | `compile.assemblies` · `compile [--platform] [--assembly] [--warnings] [--no-emit]` | Theo luật của Unity: asmdef/asmref, define constraints, version defines, platform, `overrideReferences`, plugin DLL, assembly predefined; lỗi kèm `file:line` |
| API gap | `apigap [--all] [--assembly a,b] [--from play.json,test.json] [--play scene [--frames n]] [--max n]` | API Unity mà project gọi nhưng MiniUnity chỉ có stub (`NotImplementedException`) hoặc thiếu hẳn, xếp theo số chỗ gọi, kèm `file:line` (quét IL của assembly đã compile); `--from` / `--play` thêm số lần thật sự gặp lúc chạy. `--all` gồm cả assembly của package |
| Editor UI | `menu "<Menu/Path>"` · `window <WindowType \| "Menu/Path"> [--set "Label=v;…"] [--click "Button;…"]` | Chạy code editor của project không cần Unity: `menu` gọi `[MenuItem]` (tôn trọng hàm validate), `window` mở EditorWindow rồi chạy `OnGUI` qua IMGUI headless: pass Layout → pass áp mọi `--set` (khớp theo label, `ObjectField` nhận đường dẫn asset) → một pass cho mỗi `--click` → pass Repaint; trả về danh sách control (kind, label, value, enabled, indent, options), log và exception. Control không khớp hoặc nút bị disable là lỗi. Window dùng UI Toolkit chưa hỗ trợ. Các overload `GUI` / `GUILayout` / `EditorGUI` / `EditorGUILayout` sinh bằng `tools/imgui_gen.py` (chạy lại sau `gen_stubs.sh` khi đổi bản Unity) |
| Binary asset | `serialized.dump <file> [--type T] [--name x] [--id pathID] [--yaml] [--max n]` | Đọc SerializedFile nhị phân của Unity (version 17–22, có type tree: `Library/Artifacts/**`, `Editor/Data/Resources/unity_builtin_extra`, file trong asset bundle): liệt kê object (type, pathID, tên), `--id` / `--yaml` in field dưới dạng YAML như asset text. Model (FBX, OBJ, …) mà MiniUnity không tự import được thì lấy kết quả import của Unity trong `Library/Artifacts` (mesh, material, avatar, clip, prefab của model) khi project đã mở trong Unity một lần |
| Test | `test [--mode editmode\|playmode\|all] [--filter] [--category] [--list] [--input …]` | NUnit + Unity Test Framework: `[Test]`, `[TestCase]`, `[TestCaseSource]`, `[UnityTest]`, SetUp/TearDown (cả Unity\*), OneTimeSetUp, `[Ignore]`, `[Explicit]`, `[Category]`, `LogAssert` |
| Runtime | `play <scene> [--frames n \| --seconds s] [--watch …] [--input …] [--audio-out mix.wav] [--size WxH] [--screenshot f.png[@t1,t2]] [--call "Obj/Comp.Method@t"]` | Xem bên dưới |
| Bench | `bench [--only physics3d,physics2d,raycast,animation,render3d] [--bodies n] [--frames n] [--size WxH] [--scene s.unity] [--png f.png]` | Đo ms/frame và cấp phát của tải cố định dựng trong bộ nhớ; `digest` là hash bit-chính-xác (so trước/sau khi tối ưu) |
| Log | `log.analyze <file>` | Editor.log / Player.log / log CI: lỗi compile, exception gom nhóm kèm frame đầu tiên trong project |

## Compile

`compile` dựng lại đúng cách Unity chia assembly (`Assembly-CSharp`, `-firstpass`, `-Editor`, asmdef, asmref), dùng reference
assembly của Editor đã cài (`UnityEngine.*`, `UnityEditor.*`, profile .NET Standard 2.1 / .NET 4.8) và define như Editor
(`UNITY_2022_3_*`, `UNITY_EDITOR`, platform, Scripting Define Symbols trong PlayerSettings, input handler, `UNITY_INCLUDE_TESTS`…).
Compile tăng dần: assembly không đổi lấy từ cache (`Library/MiniUnity/ScriptAssemblies`). Khi một assembly lỗi, các assembly phụ
thuộc được bỏ qua (`skippedBecause`) và DLL tốt gần nhất được giữ lại, giống `Library/ScriptAssemblies` của Unity.

Stamp của mỗi assembly dựa trên **nội dung** file nguồn (không phải mtime: `touch` hay đổi nhánh mà nội dung như cũ thì không build
lại) và **API** của assembly nó tham chiếu (hash reference assembly; compile ở chế độ deterministic): sửa thân hàm chỉ build lại đúng
assembly đó, đổi API công khai mới build lại các assembly phụ thuộc.

Trên repo này (149 assembly): build đầy đủ ~65 s, không đổi gì ~1.5 s, `touch` một file ~1.6 s (0 assembly), sửa thân hàm trong
`BlockExit.Core` ~2.3 s (1 assembly; trước đây 9 assembly, ~7 s), đổi API ~7 s (9 assembly).

## Play và test

`play` compile project, nạp DLL vào MiniUnity, nạp scene (prefab instance được "nướng" kèm override), rồi chạy player loop với
frame rate mô phỏng cố định (`--fps 60`):

Awake → OnEnable → Start (cả `IEnumerator Start`) → FixedUpdate (theo `fixedDeltaTime`; Animator `AnimatePhysics` → physics 3D →
physics 2D → OnTrigger*/OnCollision*) → Update → coroutine (`null`, `WaitForSeconds`, `WaitUntil`/`WaitWhile`, `WaitForFixedUpdate`,
coroutine lồng nhau) → `Invoke`/`InvokeRepeating` → Animator / Animation → LateUpdate → pha render của từng camera đang bật (theo
`depth`: `OnPreCull` → `OnBecameVisible` / `OnWillRenderObject` theo frustum → `OnPreRender` → `OnRenderObject` → `OnPostRender`, cùng
`Camera.onPreCull/onPreRender/onPostRender`, `Camera.current`; renderer không camera nào thấy nữa nhận `OnBecameInvisible`; không
rasterize, `MU_RENDER_CALLBACKS=0` để tắt) → `WaitForEndOfFrame` → audio (DSP clock). Khi một camera được rasterize thật (screenshot,
`record`, `Camera.Render`) chuỗi `OnRenderImage(src, dst)` của nó chạy; `Graphics.Blit` copy / co giãn, và chạy image effect viết bằng
C# nếu shader của material có trong `ImageEffects.Registry` (có sẵn `Hidden/BlitCopy`, `Unlit/Texture`, `Unlit/Color`).
`UnityEngine.Random` là xorshift128 như Unity, seed mặc định 0 (`MU_RANDOM_SEED`) để chạy lại cho cùng kết quả.
Renderer dùng quality level đang chọn trong `ProjectSettings/QualitySettings.asset` (bóng, MSAA): camera vẽ ra màn hình hoặc vào
RenderTexture tạo bằng script được MSAA 2 mẫu khi level có `antiAliasing` > 1 (như intermediate HDR của Unity; RenderTexture là
asset thì theo `antiAliasing` của chính nó). Unity Editor dùng level mặc định của build target đang chọn, nên muốn khớp ảnh chụp lúc
đang ở Android thì đặt `MU_QUALITY_LEVEL=2`; `MU_MSAA=0` tắt MSAA. Skybox (`Skybox/Procedural`, `6 Sided`, `Panoramic`) được vẽ khi
camera clear bằng Skybox, ambient mode Skybox lấy ánh sáng từ trời, Standard phản chiếu trời theo độ nhám. TextMeshPro 3D (trên
MeshRenderer) vẽ bằng shader SDF như UI.
`UnityWebRequest` chạy thật trên HttpClient (Get / Post / Put / Head / Delete, header, upload / download handler Buffer / File /
Script, redirect, `timeout`, `result` / `responseCode` / `error`, `file://`), hoàn tất trên player loop; ngoài Play mode (lệnh
`menu` / `window`) `SendWebRequest` chạy xong rồi mới trả về. **Mạng tắt mặc định**: chỉ loopback và `file://`; host khác trả
`ConnectionError` "network disabled by MiniUnity" trừ khi `MU_NETWORK=1` hoặc `--allow-network host1,host2`.
AudioMixer: volume / mute / solo / snapshot / exposed parameter như trước; mixer có effect (Lowpass / Highpass (Simple), Echo,
SFX Reverb, Compressor, Duck Volume, Send / Receive) được trộn theo khối mỗi frame qua bus của từng group, effect chạy theo thứ tự
trong group với giá trị tham số của snapshot lúc đó. Mixer chỉ có Attenuation giữ đường trộn cũ (WAV không đổi). Effect khác
(Pitch Shifter, Flange, …) cho tín hiệu đi qua và log cảnh báo một lần.
`Time.*` chạy theo thời gian mô phỏng. Exception trong code người dùng được log như Unity (loop chạy tiếp) và trả về kèm stack,
`file:line` đầu tiên trong project và `inner` (vd. `TypeInitializationException`).

```bash
./ua.sh play Assets/Scenes/Main.unity --seconds 5 --watch "Player/Health.current,Player/Transform.position" \
        --input "Space@30,mouse0@40-45,Horizontal=1@60-120,mouse=(400,300)" --audio-out /tmp/mix.wav
```

`--size 1080x1920` đặt kích thước màn hình (`Screen.width/height`, canvas Screen Space). `--screenshot out.png@1.5,3` render mọi
camera bằng software renderer ở các mốc giây (nhiều mốc → `out_1.5.png`, …). `--call "GameManager/GameController.Restart@2"` gọi
một method không tham số ở mốc giây (vd. bật bot, nạp level) – đủ để chụp lại một trạng thái game mà không cần input.

`--input` (frame tính từ 1, mục cách nhau bằng dấu phẩy): phím giữ trong khoảng frame theo KeyCode hoặc tên phím của Unity
(`Space@10`, `left shift@10-20`, `mouse0@5`); tên nút ảo bấm nút positive của nó (`Jump@12`, `Fire1@5`); `Horizontal=-1@1-60`
ghi đè giá trị axis; chuột `mouse=(400,300)` (cố định), `mouse=(400,300)@10`, kéo `mouse=(0,0)-(200,100)@10-20`, `scroll=(0,1)@15`;
chạm `touch0=(100,200)@5-9`, vuốt `touch1=(10,10)-(90,10)@20-30` (touch 0 giả lập chuột trái); joystick `joyaxis1=-1@5-30`,
`joystick button 0@7`; chữ `text=hello@40` (`Input.inputString`). Axis ảo đọc từ `ProjectSettings/InputManager.asset` với đúng luật
của Unity: sensitivity / gravity (đơn vị/giây), snap, invert, dead zone, axis chuột = delta · sensitivity, nhiều mục cùng tên lấy
giá trị tuyệt đối lớn nhất; tên axis / phím sai ném `ArgumentException` như Unity. `test --input` áp dụng cho mọi PlayMode test
(frame tính từ đầu test). Không có `--input` thì như không cắm thiết bị. Với package Input System: tay cầm `pad.leftStick=(1,0)@10-30`,
`pad.buttonSouth@5` (bí danh `a` / `cross`, `b`, `x`, `y`, `leftShoulder`, `start`, …), `pad.rightTrigger=0.5@8-12`, `pad.dpad=(0,1)@3`;
joystick `joy.stick=(0,-1)@4-9`, `joy.trigger@6`, `joy.hat=(1,0)@2`. `Gamepad.current` / `Joystick.current` chỉ có khi `--input`
dùng tới (như máy không cắm tay cầm).

### Mô phỏng runtime

| Hệ thống | Bám theo | Ghi chú |
|---|---|---|
| Physics 3D | PhysX 4.1 (Unity 2022.3): PGS solver, patch friction, speculative contact, sleep, bounce threshold | Rigidbody / Box / Sphere / Capsule / MeshCollider, CharacterController, joint (Fixed / Hinge / Spring / Character / Configurable) có projection, raycast / sweep / overlap, `Physics.Simulate`; CCD: `ContinuousSpeculative` (biên speculative theo vận tốc), `Continuous` / `ContinuousDynamic` (sweep, dừng ở va chạm đầu); `WheelCollider` (suspension spring/damper, friction curve dọc/ngang, motor/brake torque, `GetWorldPose`, `GetGroundHit`); cài đặt từ `DynamicsManager.asset` |
| Physics 2D | Box2D v2.4.1 (port, MIT) | Manifold + warm start theo feature id, block solver, NGS position solver, sleep theo island, TOI solver (`CollisionDetectionMode2D.Continuous` = bullet); Rigidbody2D, Box / Circle / Capsule / Polygon (tách lồi) / Edge / CompositeCollider2D, 9 loại Joint2D, effector, `Physics2D.*` query đủ overload; cài đặt từ `Physics2DSettings.asset` |
| Animator | Mecanim (generic): state machine, exit time, điều kiện, trigger, solo/mute, AnyState / Entry / Exit, sub-state machine, interruption, cross-fade, blend tree 1D / 2D / Direct, layer override / additive (trừ pose tham chiếu của clip: frame đầu hoặc `additiveReferencePoseClip` / `Time`), rotation blend theo quaternion (cùng bán cầu), Write Defaults | `StateMachineBehaviour`, AnimationEvent (SendMessage, báo "no receiver"), `Play`/`CrossFade`/`speed`, override controller, root motion cơ bản; legacy `Animation`; `AnimationClip.SetCurve` / `SampleAnimation`; đổi sprite (PPtr curve); AvatarMask theo transform trên layer; event của mọi clip có weight trong blend tree; `MatchTarget` / `InterruptMatchTarget` (dời root để bộ phận đích tới vị trí / hướng trong khoảng normalized time); `cullingMode` theo `Renderer.isVisible` (frustum của camera đang bật: `CullCompletely` dừng state machine, `CullUpdateTransforms` chỉ không ghi transform); API soạn thảo `UnityEditor.Animations` (`AnimatorController.CreateAnimatorControllerAtPath`, `AddState`, `CreateBlendTreeInController` / `BlendTree.AddChild`, transition + condition, `AnimationUtility.SetEditorCurve` / `SetAnimationClipSettings`) ghi ra `.controller` / `.anim` như Unity |
| Audio | Dòng thời gian phát theo `AudioSettings.dspTime` (không phụ thuộc `timeScale`) | `Play` / `PlayDelayed` / `PlayScheduled` / `PlayOneShot` / `PlayClipAtPoint`, loop, pitch, pause, `AudioListener.pause`/`volume`, rolloff 3D + pan, `GetOutputData` / `GetSpectrumData`; giải mã WAV, Ogg (NVorbis), MP3 (NLayer) trong process, không cần ffmpeg; AudioMixer: group lồng nhau, mute / solo, exposed parameter (`SetFloat`/`GetFloat`/`ClearFloat`), snapshot + `TransitionTo` theo DSP time, volume group nhân vào output; filter trên GameObject của AudioSource (`AudioLowPassFilter`, `AudioHighPassFilter`, `AudioEchoFilter` có đuôi vang sau khi clip hết, `AudioDistortionFilter`) và `OnAudioFilterRead` (gọi trong Play mode, cả AudioSource không có clip để tự sinh âm); filter trên AudioListener áp vào bản mix `--audio-out`; `play` trả `audio[]` (start/stop/end) và `--audio-out` mix ra WAV |
| Khác | | `PlayerPrefs` (lưu ở `Library/MiniUnity/PlayerPrefs.json`), nhiều scene (`SceneManager.GetSceneAt/ByPath/ByName`, `MoveGameObjectToScene`, `EditorSceneManager.OpenScene` Additive / `CloseScene`), `Camera.ScreenToWorldPoint` / `WorldToScreenPoint` / `ScreenPointToRay`, `RenderTexture` asset, `SpriteRenderer` trong ảnh chụp, `PlayableDirector` play-on-awake, `PlayableGraph.Play` (graph được evaluate mỗi frame; mọi playable đang Playing tiến thời gian theo speed của nó × speed các node cha, quá `duration` thì `IsDone`; `FrameData` có weight input và effective speed; `AudioClipPlayable` qua `AudioPlayableOutput` phát trên AudioSource đích với volume × weight input, pan, seek; `AudioMixerPlayable` chuẩn hoá volume), `CustomYieldInstruction`, `WaitForSecondsRealtime`, LitMotion runtime |
| Input | Legacy `Input` + InputManager; package Input System (`com.unity.inputsystem`) qua backend native | Xem `--input` ở trên. Với Input System, MiniUnity báo Keyboard / Mouse / Touchscreen cho package và biến `--input` thành state event (`KEYS`, `MOUS`, `TOUC`) như backend native của Unity: `Keyboard.current`, `Mouse.current`, `Touchscreen.current`, `InputAction` / composite / `PlayerInput` chạy code package thật |
| ArticulationBody | Link = body PhysX-style + khớp có lock / limit, drive giải ẩn, sau mỗi bước chiếu về toạ độ khớp (cha trước con) | Fixed / Prismatic / Revolute / Spherical, limit, drive (Force / Acceleration / Target / Velocity, stiffness lớn vẫn ổn định), `jointPosition` / `jointVelocity`, `Get/SetJointPositions`, drive target, `TeleportRoot`, collider trên link; chưa có jacobian / inverse dynamics |
| Humanoid | `AvatarBuilder.BuildHumanAvatar` (bone map) | `GetBoneTransform`, `HumanTrait`, `OnAnimatorIK` cho layer bật IK Pass, IK hai xương tay / chân (goal + hint + rotation), look-at (spine / head / eyes, clamp), `SetBoneLocalRotation`; chưa retarget clip humanoid (muscle) |
| ParticleSystem | Trạng thái particle mà script thấy được | Module `main` / `emission` (đủ property), `Emit` (số lượng / `EmitParams` / `Particle`), `Get/SetParticles`, `Play` / `Pause` / `Stop` / `Clear` / `Simulate` / `IsAlive`; trong Play mode mô phỏng mỗi frame trước LateUpdate: lifetime, vận tốc, gravity modifier, rate over time, burst, duration / loop, stop action, play on awake. Particle bay theo trục forward của emitter (không mô phỏng shape, over-lifetime, noise, collision, sub emitter, trail); renderer không vẽ particle |
| Profiler | `Profiler.Recorder` | `Recorder.Get("GC.Alloc")` đếm cấp phát managed giữa `enabled = true/false` (0 hoặc ≥ 1 block), nên `Is.Not.AllocatingGCMemory()` của Unity Test Framework chạy được; sampler khác đo thời gian |
| Player loop | `PlayerLoop.GetCurrentPlayerLoop` / `SetPlayerLoop` | system chèn thêm (Input System, UniTask…) chạy đầu pha tương ứng (Initialization, EarlyUpdate, FixedUpdate, PreUpdate, Update, PreLateUpdate, PostLateUpdate) |

Giới hạn còn lại: ArticulationBody không có jacobian / inverse dynamics (`GetDenseJacobian`, lực trọng trường / Coriolis của khớp);
Animator không retarget clip humanoid (muscle) và không đọc Avatar từ model FBX (chỉ avatar dựng bằng `AvatarBuilder`); effect
trong AudioMixer (send, lowpass của group…) không chạy, chỉ volume / mute / solo; `AudioReverbFilter` / `AudioChorusFilter` cho âm đi qua
nguyên vẹn; `OnAudioFilterRead` trên AudioListener chỉ chạy khi mix `--audio-out`.
Dấu motor của `HingeJoint2D` (dương = ngược chiều kim đồng hồ) chưa đối chiếu được với Unity. Tháp cao nhiều vật cần tăng
`solverIterations` như trong Unity.

Trong `test`, mỗi test chạy trên một scene trống mới. Assembly test chỉ có platform Editor là **EditMode** (`[UnityTest]` không
chạy player loop, như Unity); còn lại là **PlayMode** (`yield return null` = một frame, `WaitForSeconds` tua thời gian mô phỏng).
Log lỗi/exception không được `LogAssert.Expect` làm test fail (`Unhandled log message`).

Runtime có sẵn: job system (Schedule chạy đồng bộ, `JobHandle.Complete` không phải chờ), `NativeArray`, `UnsafeUtility`, Burst
(code chạy dạng C# managed), `Hash128`, uGUI/TextMeshPro, Timeline/PlayableGraph, và toàn bộ phần MiniUnity dùng để build video.

**Giới hạn.** Không có networking, input thiết bị thật; pha render trong loop chỉ gọi callback, không tạo pixel. API Unity mà MiniUnity chưa viết sẽ ném `NotImplementedException("Type.Member")`;
exception đó được đánh dấu `"miniunity": "API not implemented by MiniUnity (works in Unity)"` để tách khỏi bug của project.
Tham chiếu field chưa gán là `null` (`NullReferenceException`; Unity báo `UnassignedReferenceException`). NUnit dùng bản portable
3.5 (tải một lần từ NuGet vào `~/.nuget/packages`), vì bản Mono của Unity cần remoting `CallContext` mà .NET không có. Bản portable
được vá một lần cho khớp ABI với NUnit của Unity (`Constraint.ApplyTo(object)`, xem `Agent/NUnitAbi.cs`), nên constraint biên dịch
theo NUnit của Unity (`Is.Not.AllocatingGCMemory()`, constraint tự viết) chạy được.

## MCP server

```json
{ "mcpServers": { "miniunity": { "command": "/path/to/MiniUnity/mcp.sh", "args": ["/path/to/UnityProject"] } } }
```

Mỗi lệnh `ua` là một tool (dấu `.` đổi thành `_`: `go_tree`, `comp_set`, `pkg_resolve`…) với input
`{"args": [...positional], "options": {"name": "x", "basic": true, "position": {"x": 1}}}`, cộng tool `batch`
(`{"commands": [["go.create", "Assets/Main.unity", "A"], …]}`). Kết quả: JSON trong `content` + `structuredContent`, `isError` khi
`ok` là false. Tool chỉ đọc có `readOnlyHint`. `inputSchema` của từng tool sinh từ dòng usage: tham số vị trí theo thứ tự (bắt buộc /
tuỳ chọn / lặp lại), từng `--option` (cờ là boolean). Ảnh `play --screenshot` được trả kèm dạng image content (tối đa 4 PNG;
`"images": false` để tắt). Lệnh chỉ đọc / sửa file của project (`asset.*`, `go.*`, `comp.*` trừ
`comp.add`, `prefab.*`, `obj.*`, `meta.*`, `settings.*`, `addr.*` trừ `addr.init`, `undo`…) chạy ngay trong process của server, cache
được kiểm tra lại theo mtime mỗi lần gọi nên luôn thấy thay đổi mới nhất trên đĩa (~150–200 ms thay vì ~500 ms của một process mới
trên repo này). `compile` / `test` / `play`, `pkg.*`, `comp.add`, `so.create` chạy trong process con, nên mỗi lần đều dùng DLL vừa
build và kill được khi quá hạn. `MINIUNITY_MCP_INPROCESS=0` để chạy mọi lệnh trong process con. Tool `batch` nhận thêm
`dryRun` / `stopOnError`.

Mỗi tool nhận `timeoutSeconds` (mặc định 900 s cho `compile` / `test` / `play` / `pkg.resolve` / `pkg.add` / `batch`, 120 s cho lệnh
khác, đổi mặc định bằng `MINIUNITY_MCP_TIMEOUT`); quá hạn thì cả cây process bị kill và tool trả `isError` với lý do. Tool chạy lần lượt
trên một luồng riêng, nên `ping` vẫn được trả lời khi đang bận và `notifications/cancelled` dừng ngay lệnh đang chạy (lệnh đã huỷ không
có response, theo MCP).

**Chơi tương tác.** `play_start` (`{"scene": "Assets/Main.unity", "options": {"size": "1080x1920"}}`) mở một phiên `play
--interactive` và giữ nó: `play_input` (cú pháp `--input`, frame tính từ frame kế tiếp, mục không có `@` là bấm một frame; `"step": n`
chạy thêm n frame), `play_step` (`frames` / `seconds`), `play_screenshot` (ảnh PNG), `play_state` (frame, thời gian, log và exception
mới, `watch`), `play_stop` (trả bản tóm tắt như `play`). Ngoài MCP: `ua play <scene> --interactive` nhận lệnh JSON từng dòng trên stdin
(`step`, `input`, `hold` / `release`, `mouse`, `screenshot`, `watch`, `state`, `call`, `quit`), trả lời trên dòng bắt đầu bằng `@mu `;
`--serve 8090` mở viewer ở `http://127.0.0.1:8090/` để người chơi bằng bàn phím / chuột ngay trong trình duyệt. `OnMouseDown` / `Drag` /
`Up` / `UpAsButton` / `Enter` / `Over` / `Exit` được gửi như Unity (raycast từ camera vào collider 3D và 2D).

## Vòng làm việc gợi ý

1. `compile` → sửa lỗi theo `diagnostics[].file:line`.
2. `test` (hoặc `test --filter …`) → đọc `tests[]` fail: `message`, `file`, `line`, `stack`.
3. `play <scene> --seconds n --watch …` → xem `exceptions`, `logs`, giá trị theo dõi.
4. Sửa scene/prefab/asset bằng `go.*`, `comp.*`, `prefab.*`, `obj.*`; `asset.missing` và `mu validate` để kiểm tra tham chiếu treo.
5. `play … --screenshot` để xem màn hình game (software renderer: uGUI/TMP, SpriteRenderer, mesh 3D cơ bản).
6. Mở Unity để kiểm tra lần cuối những gì MiniUnity không mô phỏng (shader tuỳ biến, retarget humanoid, gamepad).

Ví dụ đầy đủ: game `Assets/Block Exit` (puzzle kéo khối kiểu BlockOut bằng `Rigidbody2D`) được viết, chạy và test hoàn toàn bằng
MiniUnity: `UNITY_PROJECT=$PWD/.. ./ua.sh test --filter BlockExit.Tests` (23 test: luật chơi EditMode + kéo bằng physics PlayMode).

## Kiểm thử

| Script | Nội dung |
|---|---|
| `python3 tests/ua_selftest.py` | ~135 kiểm tra trên project tạm, phủ mọi lệnh `ua`: scene, prefab override, asset / GUID / tham chiếu, package và Addressables (cần mạng; `UA_OFFLINE=1` để bỏ qua), compile / test / play / log (cần Editor), diff / undo, MCP (schema, in-process, timeout), round-trip của file đã sửa |
| `python3 tests/run_fixtures.py` | 104 fixture runtime (`tests/fixtures`: physics 3D / 2D, joint, wheel, articulation, Animator, humanoid IK, audio, mixer, input, particle, profiler) qua `ua test`; fixture nào có dòng `Run with: … --input "…"` được chạy lại với input đó |
| `python3 tests/roundtrip.py` | mọi file YAML git theo dõi được đọc rồi ghi lại giống hệt từng byte |
| `python3 tests/run_oracle.py` | **oracle test** (`tests/oracle/*.cs`): cùng phép đo chạy trên Unity thật và MiniUnity, `Oracle.Report(key, value, tolerance)`; so MiniUnity với giá trị Unity đã commit ở `tests/oracle/expected.json`. Lấy giá trị Unity: `--make-unity-project DIR` → mở bằng Unity 2022.3, chạy mọi PlayMode test (hoặc `Unity -batchmode -projectPath DIR -runTests -testPlatform PlayMode`) → `--import DIR`. Khoá chưa có giá trị Unity được liệt kê là "no reference" |
