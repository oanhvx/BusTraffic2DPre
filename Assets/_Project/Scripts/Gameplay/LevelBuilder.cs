using System.Collections.Generic;
using BusTraffic.Core;
using BusTraffic.Data;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BusTraffic.Gameplay
{
    /// <summary>
    /// LevelBuilder — dựng màn chơi (ô lưới hiển thị, xe buýt, hành khách) từ LevelData + ColorPalette.
    ///
    /// THUẬT TOÁN:
    ///   - Duyệt tuần tự cấu hình trong LevelData:
    ///       1. width x height ô → sinh GridCell_Prefab tại GridManager.GridToWorld(x, y) (chỉ là lớp hiển thị).
    ///       2. Mỗi BusPlacement → sinh Bus_Prefab → BusController.Configure (màu, ô đầu, hướng, độ dài, Material).
    ///       3. Mỗi màu trong PassengerColors → sinh Passenger_Prefab tại slot i của PassengerQueue.
    ///   - Trong Editor dùng PrefabUtility.InstantiatePrefab để giữ liên kết prefab, ghi lại prefab override
    ///     và đánh dấu Scene dirty → kết quả "nướng" (bake) sẵn vào Scene để greybox.
    ///
    /// LUỒNG XỬ LÝ:
    ///   Editor: ContextMenu "Rebuild Level From LevelData" (hoặc gọi BuildLevel qua unityMCP) → xoá con cũ của các
    ///   container → GridManager.Initialize → sinh ô / xe / hành khách → PassengerQueue.Configure + SetInitialPassengers.
    ///   Runtime: mặc định dùng bản đã bake trong Scene; nếu rebuildOnPlay = true thì dựng lại trong Awake.
    ///   (LevelData là nguồn cấu hình duy nhất — đổi LevelData rồi Rebuild để cập nhật Scene.)
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Awake: (tuỳ chọn) BuildLevel một lần. Không có Update / Coroutine — không tốn CPU sau khi dựng màn.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelBuilder : MonoBehaviour
    {
        [Header("Dữ liệu")]
        [SerializeField] private LevelData levelData;
        [SerializeField] private ColorPalette colorPalette;

        [Header("Hệ thống")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private PassengerQueue passengerQueue;

        [Header("Prefab")]
        [SerializeField] private BusController busPrefab;
        [SerializeField] private Passenger passengerPrefab;
        [SerializeField] private Transform gridCellPrefab;

        [Header("Container trong Scene")]
        [SerializeField] private Transform gridVisualRoot;
        [SerializeField] private Transform busRoot;
        [SerializeField] private Transform passengerRoot;

        [Tooltip("Dựng lại màn từ LevelData khi vào Play (mặc định dùng bản đã bake sẵn trong Scene).")]
        [SerializeField] private bool rebuildOnPlay;

        private readonly List<Passenger> spawnedPassengers = new List<Passenger>();

        private void Awake()
        {
            if (rebuildOnPlay)
            {
                BuildLevel();
            }
        }

        [ContextMenu("Rebuild Level From LevelData")]
        public void BuildLevel()
        {
            if (!ValidateReferences())
            {
                return;
            }

            gridManager.Initialize();
            passengerQueue.Configure(gridManager, gameManager, levelData.BusCapacity);

            ClearChildren(gridVisualRoot);
            ClearChildren(busRoot);
            ClearChildren(passengerRoot);

            // 1. Ô lưới hiển thị (chỉ là visual, dữ liệu nằm trong GridCell[,] của GridManager).
            for (int y = 0; y < gridManager.Height; y++)
            {
                for (int x = 0; x < gridManager.Width; x++)
                {
                    Transform tile = Spawn(gridCellPrefab, gridVisualRoot);
                    tile.name = $"Cell_{x}_{y}";
                    tile.position = gridManager.GridToWorld(new Vector2Int(x, y));
                    RecordChanges(tile);
                }
            }

            // 2. Xe buýt.
            IReadOnlyList<BusPlacement> buses = levelData.Buses;
            for (int i = 0; i < buses.Count; i++)
            {
                BusPlacement placement = buses[i];
                BusController bus = Spawn(busPrefab, busRoot);
                bus.name = $"Bus_{i + 1:00}_{placement.Color}";
                bus.Configure(placement, gridManager, gameManager, GetMaterial(placement.Color));
                RecordChanges(bus);
                RecordChanges(bus.transform);
                RecordChanges(bus.BodyRenderer);
            }

            // 3. Hành khách theo thứ tự hàng đợi (phần tử 0 = đầu hàng).
            spawnedPassengers.Clear();
            IReadOnlyList<BusColor> colors = levelData.PassengerColors;
            for (int i = 0; i < colors.Count; i++)
            {
                Passenger passenger = Spawn(passengerPrefab, passengerRoot);
                passenger.name = $"Passenger_{i + 1:00}_{colors[i]}";
                passenger.Configure(colors[i], GetMaterial(colors[i]));
                passenger.transform.position = passengerQueue.GetSlotPosition(i);
                spawnedPassengers.Add(passenger);
                RecordChanges(passenger);
                RecordChanges(passenger.transform);
                RecordChanges(passenger.BodyRenderer);
            }

            passengerQueue.SetInitialPassengers(spawnedPassengers);
            RecordChanges(passengerQueue);
            RecordChanges(gridManager);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif
            Debug.Log($"[LevelBuilder] Đã dựng {levelData.name}: lưới {gridManager.Width}x{gridManager.Height}, " +
                      $"{buses.Count} xe, {colors.Count} hành khách.", this);
        }

        private bool ValidateReferences()
        {
            bool ok = levelData != null && colorPalette != null && gridManager != null && passengerQueue != null &&
                      busPrefab != null && passengerPrefab != null && gridCellPrefab != null &&
                      gridVisualRoot != null && busRoot != null && passengerRoot != null;
            if (!ok)
            {
                Debug.LogError("[LevelBuilder] Thiếu tham chiếu — hãy gán đủ LevelData, ColorPalette, hệ thống, prefab và container.", this);
            }

            return ok;
        }

        private Material GetMaterial(BusColor color)
        {
            return colorPalette != null ? colorPalette.GetMaterial(color) : null;
        }

        private static T Spawn<T>(T prefab, Transform parent) where T : Component
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
                return instance.GetComponent<T>();
            }
#endif
            return Instantiate(prefab, parent);
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private static void RecordChanges(Object target)
        {
#if UNITY_EDITOR
            if (Application.isPlaying || target == null)
            {
                return;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }

            EditorUtility.SetDirty(target);
#endif
        }
    }
}
