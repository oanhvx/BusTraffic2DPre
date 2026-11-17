using System.Collections.Generic;
using BusTraffic.Core;
using UnityEngine;

namespace BusTraffic.Gameplay
{
    /// <summary>
    /// PassengerQueue — hàng hành khách xếp dọc theo bến đỗ và bộ ghép cặp màu hành khách ↔ xe buýt.
    ///
    /// THUẬT TOÁN:
    ///   1. Hàng đợi FIFO (First-In-First-Out) Queue&lt;Passenger&gt;: chỉ hành khách ĐẦU HÀNG được xét lên xe.
    ///   2. Ghép cặp màu: Peek().PassengerColor == dockedBus.BusColor → Dequeue() và cho lên xe;
    ///      khác màu → dừng (không được "chen hàng").
    ///   3. Điều kiện xe rời bến: currentPassengers == capacity (đầy, capacity = 4) HOẶC hàng trống
    ///      HOẶC khách đầu hàng khác màu (hết khách cùng màu liên tiếp).
    ///   4. Dồn hàng: sau mỗi lần Dequeue, hành khách thứ i được MoveTo(slot i) — cả hàng tiến lên 1 bước.
    ///      slot(i) = StationPosition + queueStartOffset + queueDirection * spacing * i.
    ///
    /// LUỒNG XỬ LÝ:
    ///   Start: nạp initialPassengers vào Queue theo thứ tự → xếp vào slot → báo số khách cho GameManager.
    ///   BusController.ArrivedAtStation → OnBusArrived(bus): lưu dockedBus, reset timer.
    ///   Mỗi 0.15s: ProcessBoardingStep → (khớp màu) Dequeue → bus.AddPassenger → passenger.BoardBus → dồn hàng
    ///   → GameManager.ReportRemainingPassengers; (không khớp / đầy / hết) → bus.Depart() và giải phóng dockedBus.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - OnEnable / OnDisable: đăng ký / huỷ đăng ký sự kiện BusController.ArrivedAtStation.
    ///   - Update mỗi frame:
    ///       nếu không có xe ở bến → return ngay (chi phí ~0);
    ///       ngược lại boardTimer += Time.deltaTime; khi boardTimer ≥ boardInterval (0.15s) → trừ đi boardInterval
    ///       và thực hiện ĐÚNG MỘT bước ProcessBoardingStep (tối đa 1 hành khách lên xe mỗi nhịp).
    ///   - Chuyển động của từng hành khách chạy trong Coroutine riêng của Passenger.
    /// </summary>
    [DisallowMultipleComponent]
    public class PassengerQueue : MonoBehaviour
    {
        [Header("Tham chiếu")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private GameManager gameManager;

        [Header("Hàng đợi")]
        [Tooltip("Thứ tự hành khách ban đầu (phần tử 0 = đầu hàng). LevelBuilder ghi từ LevelData.")]
        [SerializeField] private List<Passenger> initialPassengers = new List<Passenger>();
        [Tooltip("Sức chứa mỗi xe buýt. LevelBuilder ghi từ LevelData.BusCapacity.")]
        [SerializeField, Min(1)] private int capacity = 4;
        [Tooltip("Khoảng trễ giữa hai lượt hành khách lên xe (giây).")]
        [SerializeField, Min(0.01f)] private float boardInterval = 0.15f;

        [Header("Bố trí hàng chờ tại bến")]
        [SerializeField] private Vector3 queueStartOffset = new Vector3(1.2f, 0f, 0f);
        [SerializeField] private Vector3 queueDirection = Vector3.right;
        [SerializeField, Min(0.1f)] private float spacing = 0.6f;

        private readonly Queue<Passenger> queue = new Queue<Passenger>();
        private BusController dockedBus;
        private float boardTimer;

        public int Count => queue.Count;
        public int Capacity => capacity;

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
        }

        private void OnEnable()
        {
            BusController.ArrivedAtStation += OnBusArrived;
        }

        private void OnDisable()
        {
            BusController.ArrivedAtStation -= OnBusArrived;
        }

        private void Start()
        {
            queue.Clear();
            for (int i = 0; i < initialPassengers.Count; i++)
            {
                if (initialPassengers[i] != null)
                {
                    queue.Enqueue(initialPassengers[i]);
                }
            }

            SnapQueueToSlots();

            if (gameManager != null)
            {
                gameManager.ReportRemainingPassengers(queue.Count);
            }
        }

        private void Update()
        {
            if (dockedBus == null)
            {
                return; // Không có xe ở bến → không làm gì.
            }

            // Timer cộng dồn: mỗi boardInterval giây xử lý đúng một lượt lên xe.
            boardTimer += Time.deltaTime;
            if (boardTimer < boardInterval)
            {
                return;
            }

            boardTimer -= boardInterval;
            ProcessBoardingStep();
        }

        // ------------------------------------------------------------------
        // API công khai
        // ------------------------------------------------------------------

        /// <summary>Vị trí thế giới của slot thứ index trong hàng chờ (0 = đầu hàng, sát xe ở bến).</summary>
        public Vector3 GetSlotPosition(int index)
        {
            Vector3 basePosition = gridManager != null ? gridManager.StationPosition : transform.position;
            return basePosition + queueStartOffset + queueDirection.normalized * (spacing * index);
        }

        /// <summary>LevelBuilder gọi để gán tham chiếu hệ thống và sức chứa xe từ LevelData.</summary>
        public void Configure(GridManager grid, GameManager game, int busCapacity)
        {
            gridManager = grid;
            gameManager = game;
            capacity = Mathf.Max(1, busCapacity);
        }

        /// <summary>LevelBuilder gọi để ghi thứ tự hành khách ban đầu.</summary>
        public void SetInitialPassengers(List<Passenger> passengers)
        {
            initialPassengers.Clear();
            initialPassengers.AddRange(passengers);
        }

        // ------------------------------------------------------------------
        // Nội bộ
        // ------------------------------------------------------------------

        private void OnBusArrived(BusController bus)
        {
            if (dockedBus != null && dockedBus != bus)
            {
                Debug.LogWarning($"[PassengerQueue] {bus.name} cập bến khi {dockedBus.name} vẫn đang đón khách.", this);
            }

            dockedBus = bus;
            boardTimer = 0f;
        }

        /// <summary>
        /// Một nhịp đón khách (0.15s):
        ///   1. Nếu xe đầy / hàng trống / khách đầu hàng khác màu → cho xe rời bến.
        ///   2. Ngược lại: Dequeue khách đầu hàng → tăng số khách trên xe → khách đi lên xe → dồn hàng.
        ///   (Xe đầy được phát hiện ở nhịp kế tiếp — để khách cuối cùng có thời gian bước lên xe.)
        /// </summary>
        private void ProcessBoardingStep()
        {
            bool busFull = dockedBus.CurrentPassengers >= capacity;
            bool noMatchingPassenger = queue.Count == 0 || queue.Peek().PassengerColor != dockedBus.BusColor;

            if (busFull || noMatchingPassenger)
            {
                Debug.Log($"[PassengerQueue] {dockedBus.name} rời bến với {dockedBus.CurrentPassengers}/{capacity} khách " +
                          $"({(busFull ? "đầy xe" : "hết khách cùng màu")}).", this);
                dockedBus.Depart();
                dockedBus = null;
                boardTimer = 0f;
                return;
            }

            Passenger passenger = queue.Dequeue();
            dockedBus.AddPassenger();
            passenger.BoardBus(dockedBus.transform);
            ShiftQueueForward();

            if (gameManager != null)
            {
                gameManager.ReportRemainingPassengers(queue.Count);
            }
        }

        /// <summary>Dồn toàn bộ hàng phía sau lên 1 bước (mỗi hành khách di chuyển mượt tới slot mới).</summary>
        private void ShiftQueueForward()
        {
            int index = 0;
            foreach (Passenger passenger in queue)
            {
                passenger.MoveTo(GetSlotPosition(index));
                index++;
            }
        }

        private void SnapQueueToSlots()
        {
            int index = 0;
            foreach (Passenger passenger in queue)
            {
                passenger.transform.position = GetSlotPosition(index);
                index++;
            }
        }
    }
}
