using System;
using System.Collections;
using System.Collections.Generic;
using BusTraffic.Core;
using BusTraffic.Data;
using UnityEngine;

namespace BusTraffic.Gameplay
{
    /// <summary>
    /// BusController — điều khiển một xe buýt: đỗ trên lưới, phản hồi click, chạy theo đường BFS ra bến, đón khách rồi rời màn.
    ///
    /// THUẬT TOÁN:
    ///   1. Finite State Machine (FSM) 3 trạng thái:
    ///        Idle ──(click + có đường thoát + giữ được bến)──► MovingToStation
    ///        MovingToStation ──(tới waypoint cuối)──► BoardingAndLeaving (phát sự kiện ArrivedAtStation)
    ///        BoardingAndLeaving: đứng chờ PassengerQueue → Depart() → chờ departDelay → chạy khỏi màn → ẩn xe.
    ///      Idle ──(click nhưng bị kẹt / bến bận)──► vẫn Idle + hiệu ứng rung lắc (Shake).
    ///   2. Di chuyển theo Waypoints: mỗi frame dùng Vector3.MoveTowards (bước = moveSpeed * deltaTime) tới waypoint
    ///      hiện tại, xoay đầu xe bằng Quaternion.Slerp về hướng chuyển động; khoảng cách &lt; 0.05 thì sang waypoint kế.
    ///   3. Kiểm tra đường thoát KHÔNG dùng Physics: giao cho GridManager.FindEscapePath (BFS trên GridCell[,]).
    ///      BoxCollider chỉ phục vụ INPUT (OnMouseDown).
    ///
    /// LUỒNG XỬ LÝ:
    ///   Start: SnapToGrid → GridManager.TryOccupyCells → GameManager.RegisterBus.
    ///   OnMouseDown → TrySelect → FindEscapePath → GameManager.TryReserveStation → ReleaseCells → BuildStationRoute.
    ///   Tới bến → ArrivedAtStation (static event) → PassengerQueue cho khách cùng màu lên (AddPassenger) → Depart().
    ///   Rời bến → GameManager.ReleaseStation → chạy tới LeavePosition → GameManager.NotifyBusDeparted → Departed → SetActive(false).
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Awake: cache GridManager / GameManager / Renderer. Start: đăng ký ô chiếm với lưới.
    ///   - Frame N (Click — OnMouseDown, chạy trước Update của frame đó):
    ///       kiểm tra điều kiện → khoá input → giải phóng ô trên GridManager → nhận List&lt;Vector3&gt; worldWaypoints
    ///       → chuyển State = MovingToStation.
    ///   - Frame N+1 … N+k (Update, state MovingToStation):
    ///       step = moveSpeed * Time.deltaTime → transform.position = MoveTowards(..., waypoint, step)
    ///       → rotation = Slerp(rotation, LookRotation(hướng tới waypoint), rotateSpeed * deltaTime)
    ///       → nếu Distance &lt; 0.05: waypointIndex++ → hết waypoint: cập bến, đổi state, phát sự kiện.
    ///   - Update, state BoardingAndLeaving: đứng yên cho tới khi có Depart(); đếm lùi departDelay; rồi chạy
    ///     theo waypoint rời màn giống trên.
    ///   - Update, state Idle: return ngay (gần như 0 chi phí).
    ///   - Shake: Coroutine ShakeRoutine chạy mỗi frame trong shakeDuration giây, không đổi dữ liệu lưới.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class BusController : MonoBehaviour
    {
        public enum BusState
        {
            Idle,
            MovingToStation,
            BoardingAndLeaving
        }

        private const float WaypointReachDistance = 0.05f;

        [Header("Cấu hình xe (LevelBuilder ghi từ LevelData)")]
        [SerializeField] private BusColor busColor = BusColor.Red;
        [SerializeField] private Vector2Int headCell;
        [SerializeField] private Vector2Int forwardDirection = Vector2Int.up;
        [SerializeField, Min(1)] private int length = 2;

        [Header("Di chuyển")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 6f;
        [SerializeField, Min(0.1f)] private float rotateSpeed = 12f;
        [Tooltip("Thời gian chờ ở bến sau khi đón xong để hành khách cuối kịp lên xe.")]
        [SerializeField, Min(0f)] private float departDelay = 0.35f;

        [Header("Hiệu ứng bị kẹt (Shake)")]
        [SerializeField, Min(0.05f)] private float shakeDuration = 0.3f;
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.08f;
        [SerializeField, Min(1f)] private float shakeFrequency = 45f;

        [Header("Tham chiếu")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Renderer bodyRenderer;

        /// <summary>Phát ra khi bất kỳ xe nào cập bến — PassengerQueue lắng nghe để bắt đầu cho khách lên.</summary>
        public static event Action<BusController> ArrivedAtStation;

        /// <summary>Phát ra khi xe đã chạy khỏi màn hình.</summary>
        public event Action<BusController> Departed;

        private readonly List<Vector2Int> occupiedCells = new List<Vector2Int>();
        private List<Vector3> worldWaypoints;
        private int waypointIndex;
        private bool inputLocked;
        private bool departRequested;
        private bool stationReleased;
        private float departTimer;
        private Coroutine shakeRoutine;
        private Vector3 shakeBasePosition;

        public BusState State { get; private set; } = BusState.Idle;
        public BusColor BusColor => busColor;
        public int CurrentPassengers { get; private set; }
        public Renderer BodyRenderer => bodyRenderer;
        public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;

        // ------------------------------------------------------------------
        // Vòng đời Unity
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
            }

            if (gameManager == null)
            {
                gameManager = FindObjectOfType<GameManager>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }
        }

        private void Start()
        {
            SnapToGrid();

            if (gridManager != null && !gridManager.TryOccupyCells(occupiedCells, this))
            {
                Debug.LogError($"[BusController] {name}: không thể chiếm các ô {string.Join(", ", occupiedCells)}.", this);
            }

            if (gameManager != null)
            {
                gameManager.RegisterBus();
            }
        }

        private void OnMouseDown()
        {
            TrySelect();
        }

        private void Update()
        {
            switch (State)
            {
                case BusState.Idle:
                    return; // Đang đỗ: không làm gì mỗi frame.

                case BusState.MovingToStation:
                    if (FollowWaypoints())
                    {
                        ArriveAtStation();
                    }

                    break;

                case BusState.BoardingAndLeaving:
                    if (!departRequested)
                    {
                        return; // Đang đứng ở bến chờ PassengerQueue cho khách lên.
                    }

                    if (departTimer > 0f)
                    {
                        departTimer -= Time.deltaTime; // Cho hành khách cuối kịp bước lên xe.
                        return;
                    }

                    if (!stationReleased)
                    {
                        // Xe thực sự lăn bánh rời bến → trả bến cho xe khác.
                        stationReleased = true;
                        if (gameManager != null)
                        {
                            gameManager.ReleaseStation(this);
                        }
                    }

                    if (FollowWaypoints())
                    {
                        FinishLeaving();
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------
        // API công khai
        // ------------------------------------------------------------------

        /// <summary>
        /// FRAME N — xử lý click: kiểm tra đường thoát, khoá input, giải phóng ô, nhận Waypoints.
        /// </summary>
        public void TrySelect()
        {
            if (inputLocked || State != BusState.Idle || gridManager == null)
            {
                return;
            }

            if (gameManager != null && gameManager.State != GameManager.GameState.Playing)
            {
                return;
            }

            // 1) Hỏi GridManager đường thoát (BFS trên GridCell[,]).
            List<Vector2Int> path = gridManager.FindEscapePath(occupiedCells, forwardDirection);
            if (path == null)
            {
                Debug.Log($"[BusController] {name} bị kẹt — không có đường ra Exit Row.", this);
                PlayShake();
                return;
            }

            // 2) Chỉ một xe được dùng bến tại một thời điểm.
            if (gameManager != null && !gameManager.TryReserveStation(this))
            {
                Debug.Log($"[BusController] {name}: bến đang có xe khác, hãy chờ.", this);
                PlayShake();
                return;
            }

            // 3) Khoá input của xe này và dừng hiệu ứng rung (nếu còn).
            inputLocked = true;
            StopShake();

            // 4) Giải phóng NGAY các ô cũ trên lưới để xe khác có thể đi qua.
            gridManager.ReleaseCells(occupiedCells, this);
            occupiedCells.Clear();

            // 5) Đổi đường ô lưới → Waypoints thế giới và chuyển trạng thái.
            worldWaypoints = gridManager.BuildStationRoute(path);
            waypointIndex = 0;
            State = BusState.MovingToStation;
        }

        /// <summary>PassengerQueue gọi mỗi khi có một hành khách cùng màu lên xe.</summary>
        public void AddPassenger()
        {
            CurrentPassengers++;
        }

        /// <summary>PassengerQueue gọi khi xe đầy hoặc hết khách cùng màu → xe rời bến.</summary>
        public void Depart()
        {
            if (State != BusState.BoardingAndLeaving || departRequested)
            {
                return;
            }

            departRequested = true;
            departTimer = departDelay;
            worldWaypoints = new List<Vector3>(1) { gridManager.LeavePosition };
            waypointIndex = 0;
        }

        /// <summary>Ghi cấu hình từ LevelData (dùng bởi LevelBuilder, cả trong Editor lẫn lúc chạy).</summary>
        public void Configure(BusPlacement placement, GridManager grid, GameManager game, Material material)
        {
            busColor = placement.Color;
            headCell = placement.HeadCell;
            forwardDirection = placement.Forward;
            length = placement.Length;
            gridManager = grid;
            gameManager = game;

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (bodyRenderer != null && material != null)
            {
                bodyRenderer.sharedMaterial = material;
            }

            SnapToGrid();
        }

        /// <summary>Đặt xe vào đúng tâm các ô chiếm và xoay theo hướng đầu xe (đọc từ dữ liệu lưới, không ngược lại).</summary>
        public void SnapToGrid()
        {
            BuildOccupiedCells();
            if (gridManager == null || occupiedCells.Count == 0)
            {
                return;
            }

            Vector3 center = Vector3.zero;
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                center += gridManager.GridToWorld(occupiedCells[i]);
            }

            center /= occupiedCells.Count;

            Vector2Int dir = forwardDirection == Vector2Int.zero ? Vector2Int.up : forwardDirection;
            Vector3 forwardWorld = new Vector3(dir.x, 0f, dir.y);
            transform.SetPositionAndRotation(center, Quaternion.LookRotation(forwardWorld, Vector3.up));
        }

        // ------------------------------------------------------------------
        // Nội bộ
        // ------------------------------------------------------------------

        private void BuildOccupiedCells()
        {
            occupiedCells.Clear();
            for (int i = 0; i < length; i++)
            {
                occupiedCells.Add(headCell - forwardDirection * i);
            }
        }

        /// <summary>
        /// FRAME N+1 … N+k — tiến một bước theo Waypoints. Trả về true khi đã tới waypoint cuối.
        /// </summary>
        private bool FollowWaypoints()
        {
            if (worldWaypoints == null || waypointIndex >= worldWaypoints.Count)
            {
                return true;
            }

            Vector3 target = worldWaypoints[waypointIndex];
            float step = moveSpeed * Time.deltaTime;

            // Xoay đầu xe mượt về hướng di chuyển (bỏ trục Y để xe không chúi đầu).
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(toTarget, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, rotateSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, target, step);

            if (Vector3.Distance(transform.position, target) < WaypointReachDistance)
            {
                waypointIndex++;
                if (waypointIndex >= worldWaypoints.Count)
                {
                    transform.position = target; // khử sai số tại đích
                    return true;
                }
            }

            return false;
        }

        private void ArriveAtStation()
        {
            State = BusState.BoardingAndLeaving;
            worldWaypoints = null;
            departRequested = false;
            stationReleased = false;
            ArrivedAtStation?.Invoke(this);
        }

        private void FinishLeaving()
        {
            worldWaypoints = null;
            if (gameManager != null)
            {
                gameManager.NotifyBusDeparted();
            }

            Departed?.Invoke(this);
            gameObject.SetActive(false);
        }

        private void PlayShake()
        {
            StopShake();
            shakeBasePosition = transform.position;
            shakeRoutine = StartCoroutine(ShakeRoutine());
        }

        private void StopShake()
        {
            if (shakeRoutine == null)
            {
                return;
            }

            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
            transform.position = shakeBasePosition;
        }

        /// <summary>Rung lắc dọc trục đầu xe với biên độ giảm dần (sin * damper) rồi trả về vị trí gốc.</summary>
        private IEnumerator ShakeRoutine()
        {
            Vector3 axis = transform.forward;
            float elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                elapsed += Time.deltaTime;
                float damper = 1f - Mathf.Clamp01(elapsed / shakeDuration);
                float offset = Mathf.Sin(elapsed * shakeFrequency) * shakeAmplitude * damper;
                transform.position = shakeBasePosition + axis * offset;
                yield return null;
            }

            transform.position = shakeBasePosition;
            shakeRoutine = null;
        }
    }
}
