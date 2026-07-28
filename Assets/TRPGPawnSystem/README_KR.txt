TRPG Pawn System v2
===================

1. 적용 위치
-----------

이 ZIP을 Unity 프로젝트의 Assets 폴더 안에 압축 해제한다.

최종 경로:

Assets/TRPGPawnSystem/
  Resident/
  SO/
  Prefab/
  Manager/

ZIP에는 Assets 폴더, asmdef, Scene, Prefab 에셋이 중첩되어 있지 않다.
이전 Pawn.cs 또는 같은 이름의 클래스가 남아 있다면 새 파일과 함께 두지 않는다.


2. 필요한 기존 패키지
--------------------

- Unity Input System
- DOTween
- NavMeshPlus

패키지는 ZIP에 포함하지 않으며 ProjectSettings도 변경하지 않는다.


3. 데이터 에셋 생성
------------------

Project 창 우클릭 > Create 메뉴에서 아래 에셋을 만든다.

1) Trpg/Pawn/Pawn System Settings
   - 이동 거리, 5ft 격자, Door Guard, 하단 UI 크기와 속도를 설정한다.

2) Trpg/Pawn/Interactive Pawn Definition
   - Kind: Moveable / Npc / Door
   - Moveable Kind: Player / Monster
   - Display Name, Description, Portrait
   - Move Meters, 이동 연출 속도

3) Trpg/Pawn/Field Pawn Definition
   - Kind: Floor / Obstacle
   - Floor 목적지 사용 여부
   - 이동 가능 오버레이 색과 Fade 시간

Field Pawn Definition에는 Portrait, 이름, 설명 데이터가 없다.


4. GameScene01 권장 계층
----------------------

GameScene01
  Systems
    PawnNavMeshManager
    PawnMovementManager
    PawnManager
    PawnUIManager
  Navigation
    NavMeshSurface
  Pawns
    Moveable / NPC / Door / Floor / Obstacle 오브젝트들


5. Manager 연결
--------------

PawnNavMeshManager
  Surface              = NavMeshPlus의 NavMeshSurface
  Build On Start       = 켜면 시작 다음 프레임 자동 Bake

PawnMovementManager
  Settings             = PawnSystemSettings 에셋
  Nav Mesh Manager     = PawnNavMeshManager

PawnManager
  Pawn Root            = Pawns 부모 Transform
  Board Camera         = Orthographic Camera
  Pawn Layer Mask      = Pawn Collider가 속한 Layer
  Movement Manager     = PawnMovementManager

PawnUIManager
  Pawn Manager         = PawnManager
  Settings             = PawnSystemSettings 에셋
  Info Bar             = 비워두면 런타임에 화면 하단 바를 자동 생성


6. Pawn 설정
------------

InteractivePawn
  - Player, Monster, 일반 NPC, Door에 사용한다.
  - Definition에 Interactive Pawn Definition을 넣는다.
  - Instance Id는 씬 전체에서 고유해야 한다.
  - Visual Root에는 이동 연출할 Sprite 자식을 넣는다.
  - 클릭하면 Portrait, 이름, 설명 하단 바가 열린다.

FieldPawn
  - Floor와 Obstacle에 사용한다.
  - Definition에 Field Pawn Definition을 넣는다.
  - 클릭해도 Portrait UI가 열리지 않는다.
  - Floor는 자동으로 Walkable Modifier를 사용한다.
  - Obstacle은 자동으로 Not Walkable Modifier를 사용한다.
  - 자기 자신 또는 자식에 Collider2D가 있으면 그대로 쓴다.
  - Collider2D가 하나도 없을 때만 BoxCollider2D를 추가한다.


7. Door 연결
------------

Door A:
  Instance Id             = door_a
  Linked Door Instance Id = door_b
  Arrival Point           = A로 들어올 때 도착할 Transform

Door B:
  Instance Id             = door_b
  Linked Door Instance Id = door_a
  Arrival Point           = B로 들어올 때 도착할 Transform

Arrival Point는 Door Trigger 바깥이면서 NavMesh 위에 둔다.
Moveable이 Door Trigger에 겹치면 연결된 Door의 Arrival Point로 이동한다.


8. 동작 흐름
------------

- Moveable 클릭:
  하단 정보 바 표시 + 이동 가능한 Floor 오버레이 표시

- NPC 또는 Door 클릭:
  하단 정보 바 표시 + 이동 Floor 오버레이 숨김

- 이동 가능한 Floor 클릭:
  선택된 Moveable의 이동력을 먼저 차감하고 NavMesh 경로로 DOTween 연출

- Obstacle 위 클릭:
  아래 Floor 클릭을 차단

- 턴 시작:
  PawnMovementManager.ResetMovementBudget(pawn) 또는
  PawnMovementManager.ResetAllMovementBudgets() 호출


9. 구현 경계
------------

- Pawn: 공통 Instance Id와 선택 표시
- InteractivePawn: Portrait 데이터, NPC/Moveable/Door 표시와 Trigger 이벤트
- FieldPawn: Floor/Obstacle 및 NavMesh 형상
- PawnManager: Input System 클릭, 선택, 상호작용 요청
- PawnMovementManager: 이동력, 경로 캐시, Door 전송
- PawnNavMeshManager: Bake, 투영, 경로 계산
- PawnUIManager: Interactive 선택을 정보 바로 전달
- PawnInfoBarWidget: 전달받은 값 표시와 DOTween 연출만 담당

DOTween은 연출 전용이다. 이동력과 목적지 상태는 연출 시작 전에 확정한다.
DOColor, DOFade, DOAnchorPos, DOMove 같은 모듈 확장 메서드는 사용하지 않는다.
