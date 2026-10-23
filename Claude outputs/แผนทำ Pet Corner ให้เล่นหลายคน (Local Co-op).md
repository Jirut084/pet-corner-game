# แผนทำ Pet Corner ให้เล่นหลายคน (Local Co-op บนเครื่องเดียว)

สรุปสั้นๆ ก่อน: **แนะนำให้เปลี่ยนไปใช้ Input System Package (New Input System)** สำหรับสคริปต์ที่เกี่ยวกับการควบคุมผู้เล่นโดยตรง เพราะปัญหาหลักของ local co-op คือต้อง "แยกปุ่ม" ของผู้เล่นแต่ละคนออกจากกัน (เช่น คนที่ 1 ใช้ WASD, คนที่ 2 ใช้ลูกศร) ซึ่ง Legacy Input Manager ทำได้แต่จะรกและไม่รองรับเกมแพดในอนาคต ข่าวดีคือ**ไม่ต้องเรียนรู้ใหม่ทั้งหมด** เพราะในโฟลเดอร์แล็บของคุณเอง (`Dev3/Assets/Scripts/Chapter3/CapsulePlayerControllerV1.cs` และ `Chapter5/InteractionInput.cs`) มีแพทเทิร์น "แยก Action Map ต่อผู้เล่นบนคีย์บอร์ดเดียว" (Player 1 / Player 2) อยู่แล้ว เอามาปรับใช้กับ Pet Corner ได้ตรงๆ

## ทำไมต้องเปลี่ยน Input System

ตอนนี้ Pet Corner ทุกสคริปต์ที่รับ input เรียก `UnityEngine.Input` แบบ static ตรงๆ:
- `PlayerController.HandleMovement()` → `Input.GetAxisRaw("Horizontal"/"Vertical")`
- `InteractionManager.Update()` → `Input.GetKeyDown(interactKey)` (ค่าเริ่มต้น `KeyCode.E`)
- `TilePlacer.Update()` → `Input.GetKeyDown(grabKey)` (ค่าเริ่มต้น `KeyCode.G`)
- `CameraController.UpdateThirdPerson()` → `Input.GetAxis("Mouse X"/"Mouse Y")`

ปัญหาคือถ้า spawn ผู้เล่น 2 ตัว ทั้งสองตัวจะอ่าน `Input.GetAxisRaw("Horizontal")` **ตัวเดียวกัน** เพราะเป็นแกนที่ตั้งค่าไว้ใน Project Settings กลาง ไม่มีทางแยก "WASD ของคนที่ 1" ออกจาก "ลูกศรของคนที่ 2" ได้โดยไม่เพิ่มโค้ดพิเศษ ส่วน `interactKey`/`grabKey` แม้จะเป็น `[SerializeField]` ที่ตั้งค่าต่างกันต่ออินสแตนซ์ได้ แต่ก็ยังต้องเขียนระบบ mapping เองอยู่ดี — ซึ่งพอเขียนไปเขียนมาจะกลายเป็นสิ่งที่ Input System ให้มาฟรีอยู่แล้ว (Control Scheme + Action Map ต่อผู้เล่น)

ถ้าอยากลองแบบไม่แตะ Input System เลยก็พอทำได้แบบง่ายๆ (ดูหัวข้อ "ทางลัดแบบไม่เปลี่ยน Input System" ท้ายเอกสาร) แต่จะไม่ scale ไปรองรับเกมแพดทีหลัง

## ไฟล์ที่ต้องแก้ไข

### ต้องแก้ไข (เกี่ยวกับ input ของผู้เล่นโดยตรง)

**`PlayerController.cs`**
เพิ่มฟิลด์ `_actionMapName` (ค่าเริ่มต้น `"Player 1"`) แบบเดียวกับ `CapsulePlayerControllerV1.m_ActionMapName` ผูก `InputAction` ชื่อ `Move` จาก Action Map นั้น (`InputSystem.actions.FindActionMap(mapName).FindAction("Move")`) แล้วอ่านค่าด้วย `ReadValue<Vector2>()` แทน `Input.GetAxisRaw` สองบรรทัดเดิม ที่เหลือ (การคำนวณทิศทางตามกล้อง, `CharacterController.Move`, gravity) ไม่ต้องแก้เลย

**`InteractionManager.cs`**
นี่คือจุดที่เอาโค้ดจาก `Chapter5/InteractionInput.cs` มาปรับใช้ได้ตรงที่สุด เพราะโครงสร้างเดิมทำเรื่องเดียวกัน (ผูก Action `Interact` จาก Action Map ที่ resolve ได้, subscribe `performed`) สิ่งที่ต้องเพิ่มจากของเดิมคือ Pet Corner มีโหมด "กดค้าง" (`IHoldInteractable`) ด้วย ซึ่ง `InteractionInput.cs` ต้นฉบับไม่มี ต้องเก็บสถานะ "กำลังกดอยู่ไหม" เองแทนการเช็ค `Input.GetKey(interactKey)` ในลูป `UpdateActiveHold()` — วิธีที่ตรงไปตรงมาที่สุดคือติดตาม `started`/`canceled` ของ `InputAction` มาอัปเดต `bool _interactHeld` แล้วให้ `UpdateActiveHold()` เช็คตัวแปรนี้แทน

**`TilePlacer.cs`**
เดิมใช้ `Input.GetKeyDown(grabKey)` ตัวเดียว เปลี่ยนเป็นผูก Action ใหม่ชื่อ `Place` (ต้องเพิ่มเข้า Input Actions asset เพราะยังไม่มี) จาก Action Map ของผู้เล่นคนนั้น อ่านแบบ edge-trigger ด้วย `WasPressedThisFrame()`

**`CameraController.cs`**
จุดนี้ไม่ใช่แค่เรื่อง input system แต่เป็นปัญหาการออกแบบกล้องสำหรับ 2 คน: โหมด `ThirdPerson` ใช้เมาส์มองรอบตัว (`Input.GetAxis("Mouse X/Y")`) ซึ่งมีเมาส์แค่ตัวเดียวในเครื่อง แชร์ให้ 2 คนพร้อมกันไม่ได้อยู่ดี ไม่ว่าจะเปลี่ยน input system หรือไม่ แนะนำ 2 ทาง:
- **จอเดียวกัน (เหมือน PlateUp!/Overcooked)**: ใช้โหมด `TopDown2_5D` ที่มีอยู่แล้วในสคริปต์นี้ (ไม่ต้องใช้เมาส์เลย) แล้วเขียนกล้องใหม่ 1 ตัวที่มอง "จุดกึ่งกลางระหว่างผู้เล่นทั้งสอง" พร้อมซูมออกอัตโนมัติตามระยะห่าง (สคริปต์ใหม่ ยังไม่มีในโปรเจกต์ตอนนี้)
- **แบ่งจอ (split-screen)**: ใช้ `CameraController` เดิมได้เกือบเป๊ะ แต่ปิด mouse look ทิ้ง (ใช้ `TopDown2_5D` หรือ third-person แบบ fixed offset ไม่หมุนตามเมาส์) แล้วสร้าง `Camera` 2 ตัว ตั้ง `Viewport Rect` ครึ่งจอคนละครึ่ง คนละตัวตาม target ของตัวเอง

### ต้องเพิ่มใหม่ (ยังไม่มีไฟล์นี้ในโปรเจกต์)

**ตัว spawn ผู้เล่น** — เพราะตอนนี้ฉากคงมี Player อยู่ตัวเดียวที่วางไว้ล่วงหน้า ต้องมีจุด spawn ตัวที่สอง และตั้งค่า `_actionMapName = "Player 2"` ให้กับอินสแตนซ์นั้น เนื่องจากผู้เล่นทั้งสองแชร์คีย์บอร์ดตัวเดียว (ไม่ใช่คนละอุปกรณ์) การใช้ `PlayerInputManager` แบบ auto-join ตามอุปกรณ์ (ที่บทเรียน New Input System สอนไว้) จะไม่เหมาะเท่ากับการ instantiate 2 ตัวตรงๆ แล้วกำหนด Action Map ให้แต่ละตัวเอง (เหมือนที่ Lab บทที่ 3 ทำกับ `CapsulePlayerControllerV1`) เขียนเป็นสคริปต์เล็กๆ คล้าย `InventoryPlayerBootstrap.cs` ในแล็บ Chapter4 ของคุณ หรือจะให้ `GameManager`/`LevelInitializer` เป็นคน spawn ก็ได้

**Input Actions asset** — ต้องเพิ่ม Action Map "Player 2" (duplicate จาก "Player 1" แล้วเปลี่ยน key binding เป็นลูกศร/RightShift/RightCtrl) ตามสูตรที่ทำไว้แล้วในเอกสารบทที่ 3 (Lab 3.1.1.1) พร้อมเพิ่ม Action `Interact` และ `Place` เข้าไปทั้งสอง Action Map (ของเดิมใน Lab มีแค่ Move/Sprint/Jump ยังไม่มี 2 action นี้เพราะ Pet Corner ต้องใช้เพิ่ม)

### ไม่ต้องแก้ (ออกแบบมาให้ไม่ผูกกับผู้เล่นคนเดียวอยู่แล้ว)

กลุ่มนี้รับ `GameObject interactor`/`actor` เป็นพารามิเตอร์อยู่แล้ว หรือเป็นระบบกลางของร้านที่ทั้งสองคนใช้ร่วมกันโดยธรรมชาติ (แบบเดียวกับที่ PlateUp!/Overcooked ให้ทุกคนช่วยกันทำออเดอร์เดียวกัน) จึงไม่ต้องแตะเลย:

`IntakePoint.cs`, `CareStation.cs`, `ReturnZone.cs`, `EquipmentParcel.cs`, `PlayerCarrier.cs` (เป็น component ต่ออินสแตนซ์อยู่แล้ว แต่ละผู้เล่นถือของตัวเองเป็นอิสระ), `GameManager.cs`, `CustomerManager.cs`, `Customer.cs`, `Pet.cs`, `PetSpecies.cs`, `TileGrid.cs`, `GridOccupant.cs`, `LevelInitializer.cs`, `StationCatalog.cs`, `PlayerProgress.cs`, `DayEndRewardSystem.cs`, `GameLog.cs`, `MoneyUIDisplay.cs`

### ทางเลือก (ไม่จำเป็นสำหรับ core gameplay)

`GameFlowDebugControls.cs` ยังใช้ `Input.GetKeyDown` ของเมนู/เลือกรางวัลได้ตามเดิม เพราะเป็นปุ่มที่ "ใครกดก็ได้" ไม่ต้องแยกผู้เล่น ถ้าอยากทำให้ครบก็ค่อยย้ายมาใช้ Input System UI Action Map ทีหลังได้

## จุดที่ควรทดสอบหลังแก้

- สองคนพยายามกดสถานี/ช่องกริดเดียวกันพร้อมกัน — โค้ดปัจจุบันน่าจะรอดเพราะมีการเช็คเงื่อนไขก่อนทำงานทุกจุด (เช่น `IsOccupied`, `carrier.IsCarrying`) แต่ควรลองเล่นจริงเพื่อเช็ค edge case
- `ConcurrentOrderLimit`/เวลาความอดทนของ `PetSpecies` อาจต้องปรับตัวเลขขึ้น เพราะ 2 คนทำงานเร็วกว่าคนเดียวมาก
- ถ้าทำจอเดียวกัน (ไม่ split-screen) ต้องเช็คว่ากล้องซูมออกพอเห็นทั้งสองคนตลอดเวลาที่เดินห่างกัน

## ทางลัดแบบไม่เปลี่ยน Input System (ถ้าอยากลองเร็วๆ ก่อน)

ถ้าแค่อยากเทสต์ว่า 2 คนเล่นด้วยกันสนุกไหมก่อนลงทุนทำ Input System เต็มรูปแบบ ทำแบบง่ายได้เหมือนกัน: เพิ่มฟิลด์ `KeyCode` แยกทิศ (`up/down/left/right/interact/grab`) ใน `PlayerController`/`InteractionManager`/`TilePlacer` แทนการใช้ชื่อแกน `"Horizontal"/"Vertical"` แล้วเช็คด้วย `Input.GetKey(upKey)` ตรงๆ ทีละปุ่ม จากนั้นตั้งค่าปุ่มต่างกันในอินสแตนซ์ที่สองใน Inspector วิธีนี้ใช้ได้จริงและไม่ต้องแตะ Input System เลย แต่ข้อเสียคือรองรับแค่คีย์บอร์ด (ไม่มีเกมแพด) และโค้ดจะดูรกกว่าถ้าจะขยายเป็น 3–4 คนทีหลัง — เหมาะเป็นแค่ prototype ทดสอบไอเดียก่อนตัดสินใจลงแรงทำแบบเต็ม

---

ถ้าต้องการให้ผมเริ่มแก้ไขโค้ดจริงตามแผนนี้ (เช่น `PlayerController.cs`/`InteractionManager.cs`/`TilePlacer.cs` และสคริปต์ spawn ผู้เล่นใหม่) บอกได้เลยครับ จะเริ่มจากไฟล์ไหนก่อนก็ได้
