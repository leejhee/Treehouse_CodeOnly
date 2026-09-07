#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;

namespace TreeHouse.PerformanceBenchmark
{
    public enum BenchmarkScenario
    {
        FieldStatic = 0,
        HouseMixed24Static = 1
    }

    public struct BenchmarkScenarioDetails
    {
        public string Id;
        public string DisplayName;
        public int FieldObjectCount;
        public int HouseFurnitureCount;
        public int HouseCarpetCount;
    }

    /// <summary>
    /// Supplies a repeatable, benchmark-only save state. Performance APKs use a
    /// separate application ID, so any save performed by the original game code
    /// remains isolated from the production application.
    /// </summary>
    public static class BenchmarkScenarioFixture
    {
        public const int Seed = 20230901;
        public const string FixtureVersion = "fixture-v1";
        private const int HouseFurnitureCount = 24;

        public static void PrepareSaveBeforeStart()
        {
            UnityEngine.Random.InitState(Seed);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.saveData = CreateSaveData();
            }
        }

        public static BenchmarkScenarioDetails GetDetails(BenchmarkScenario scenario)
        {
            if (scenario == BenchmarkScenario.HouseMixed24Static)
            {
                return new BenchmarkScenarioDetails
                {
                    Id = "HOUSE_MIXED_24_STATIC_V1",
                    DisplayName = "House static / 24 furniture (4 carpets)",
                    FieldObjectCount = 62,
                    HouseFurnitureCount = HouseFurnitureCount,
                    HouseCarpetCount = 4
                };
            }

            return new BenchmarkScenarioDetails
            {
                Id = "FIELD_STATIC_V1",
                DisplayName = "Field static / fixed spawned items",
                FieldObjectCount = 62,
                HouseFurnitureCount = HouseFurnitureCount,
                HouseCarpetCount = 4
            };
        }

        public static bool TryApply(BenchmarkScenario scenario, out string failureReason)
        {
            if (GameManager.Instance == null || WarpManager.instance == null ||
                ArrangeManager.instance == null || CharacterControl.instance == null)
            {
                failureReason = "scenario dependencies are not ready";
                return false;
            }

            if (GameManager.Instance.saveData == null ||
                GameManager.Instance.saveData.houseList == null ||
                GameManager.Instance.saveData.houseList.Count != 1)
            {
                GameManager.Instance.saveData = CreateSaveData();
            }

            WarpManager warp = WarpManager.instance;
            GameObject character = CharacterControl.instance.gameObject;
            CharacterController controller = character.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            if (scenario == BenchmarkScenario.HouseMixed24Static)
            {
                WarpManager.nowWatchingHouse = 1;
                WarpManager.nowHouseArea = 1;
                ArrangeManager.instance.LoadFixedFurniture();

                warp.isField = false;
                warp.fieldRoot.SetActive(false);
                warp.houseRoot.SetActive(true);
                character.transform.SetPositionAndRotation(
                    new Vector3(5004f, 5f, 0f), Quaternion.Euler(0f, 90f, 0f));

                if (ArrangeManager.instance.fixedFurnitureList.Count != HouseFurnitureCount)
                {
                    failureReason = "house fixture furniture count mismatch";
                    RestoreController(controller);
                    return false;
                }
            }
            else
            {
                WarpManager.nowWatchingHouse = -1;
                WarpManager.nowHouseArea = 1;
                warp.isField = true;
                warp.houseRoot.SetActive(false);
                warp.fieldRoot.SetActive(true);
                character.transform.SetPositionAndRotation(
                    new Vector3(0f, 2f, 0f), Quaternion.identity);
            }

            RestoreController(controller);
            Physics.SyncTransforms();
            failureReason = string.Empty;
            return true;
        }

        private static void RestoreController(CharacterController controller)
        {
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        private static SaveDataClass CreateSaveData()
        {
            SaveDataClass save = new SaveDataClass
            {
                userName = "PERFORMANCE_BENCHMARK",
                coin = 1000,
                // A future timestamp prevents FieldManager's regeneration pass from
                // adding a random number of objects before it instantiates this list.
                lastEndTime = DateTime.Now.AddDays(1)
            };
            save.dateTimeText = save.lastEndTime.ToString("O");

            AddFieldItem(save, 1, 2);
            AddFieldItem(save, 2, 1);
            AddFieldItem(save, 3, 1);
            AddFieldItem(save, 4, 1);
            AddFieldItem(save, 5, 1);
            AddFieldItem(save, 6, 1);
            AddFieldItem(save, 7, 1);
            AddFieldItem(save, 8, 2);
            AddFieldItem(save, 9, 2);
            AddFieldItem(save, 10, 1);
            AddFieldItem(save, 11, 1);
            AddFieldItem(save, 12, 1);
            AddFieldItem(save, 13, 1);
            AddFieldItem(save, 15, 44);
            AddFieldItem(save, 16, 1);
            AddFieldItem(save, 17, 1);

            HouseFeature house = new HouseFeature { id = 1 };
            int[] itemIds =
            {
                6, 17, 6, 17,
                1, 2, 3, 4, 5, 7, 8, 9,
                16, 1, 2, 3, 4, 5, 7, 8,
                9, 16, 1, 7
            };

            for (int index = 0; index < itemIds.Length; index++)
            {
                int column = index % 6;
                int row = index / 6;
                int itemId = itemIds[index];
                house.furnitureList.Add(new FurnitureFeature
                {
                    arrangeNo = index,
                    id = itemId,
                    x = 5001.5f + column * 3.2f,
                    y = itemId == 6 || itemId == 17 ? 0.5f : 1.5f,
                    z = -7.5f + row * 5f,
                    yRotation = 0f
                });
            }

            save.houseList.Add(house);
            return save;
        }

        private static void AddFieldItem(SaveDataClass save, int id, int count)
        {
            save.fieldItemList.Add(new SaveDataClass.FieldItem
            {
                id = id,
                count = count
            });
        }
    }
}
#endif
