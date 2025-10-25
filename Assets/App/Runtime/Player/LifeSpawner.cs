using UnityEngine;

namespace App.Runtime.Player
{
    public class LifeSpawner : MonoBehaviour
    {
        public static LifeSpawner Instance { get; private set; }

        [Header("Spawn Prefabs")] public GameObject preyPrefab;

        public GameObject predatorPrefab;

        [Header("Spawn counts")] public int initialPrey = 50;
        public int initialPredator = 5;
        public float spawnInterval = 3.0f;
        public int maxPrey = 200;
        public int maxPredator = 20;
        public float predatorSpawnProbability = 0.1f; // Predatorがスポーンする確率
        public int preyToPredatorRatio = 5; // Predator1匹に対して必要なPreyの数

        [Header("Spawn area")] public Vector2 areaSize = new(30f, 30f);
        public Vector2 ignoreAreaSize = new(0f, 0f);
        public bool useLocalSpace = false;

        [Header("Genome randomness (Prey)")] public Vector2 preySpeedRange = new(0.5f, 2.0f);
        public Vector2 preyViscosityRange = new(0.5f, 2.5f);
        public Vector2 preySizeRange = new(0.3f, 1.6f);
        public Vector2 preyHueRange = new(0f, 1f);
        public Vector2 preyEmissionRange = new(0.1f, 1f);

        [Header("Genome randomness (Predator)")]
        public Vector2 predSpeedRange = new(0.8f, 2.8f);

        public Vector2 predViscosityRange = new(0.5f, 2.0f);
        public Vector2 predSizeRange = new(0.8f, 2.5f);
        public Vector2 predHueRange = new(0f, 1f);
        public Vector2 predEmissionRange = new(0.2f, 1.2f);

        private float timer;
        private int preyCount;
        private int predatorCount;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
        
        private void Start()
        {
            for (var i = 0; i < initialPrey; i++) SpawnPrey();
            for (var i = 0; i < initialPredator; i++) SpawnPredator();
            timer = spawnInterval;
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                TrySpawn();
                timer = spawnInterval;
            }
        }

        private void TrySpawn()
        {
            // プレデターのスポーン試行
            bool predatorSpawned = false;
            if (predatorCount < maxPredator && preyCount > predatorCount * preyToPredatorRatio && Random.value < predatorSpawnProbability)
            {
                SpawnPredator();
                predatorSpawned = true;
            }

            // プレデターがスポーンしなかった場合、またはPreyが上限に達していない場合にPreyをスポーン
            if (!predatorSpawned && preyCount < maxPrey)
            {
                SpawnPrey();
            }
        }

        /// <summary>
        /// 新しいPreyをスポーンします。
        /// </summary>
        private void SpawnPrey()
        {
            if (preyCount >= maxPrey) return;
            if (!preyPrefab) return;
            var pos = RandomPosition();
            var go = Instantiate(preyPrefab, pos, Quaternion.identity);
            preyCount++;

            var prey = go.GetComponent<PreyAgent>();
            if (prey)
            {
                InitializeGenome(prey.genome, preySpeedRange, preyViscosityRange, preySizeRange, preyHueRange, preyEmissionRange);
                prey.nutrition = Random.Range(0.5f, 2.0f);
            }

            go.AddComponent<PopulationTracker>().Init(this, false);
        }

        /// <summary>
        /// 新しいPredatorをスポーンします。
        /// </summary>
        private void SpawnPredator()
        {
            if (predatorCount >= maxPredator) return;
            if (!predatorPrefab) return;
            var pos = RandomPosition();
            var go = Instantiate(predatorPrefab, pos, Quaternion.identity);
            predatorCount++;

            var pred = go.GetComponent<PredatorAgent>();
            if (pred)
            {
                InitializeGenome(pred.genome, predSpeedRange, predViscosityRange, predSizeRange, predHueRange, predEmissionRange);
                if (pred.preyMask == 0) pred.preyMask = LayerMask.GetMask("Prey");
            }

            go.AddComponent<PopulationTracker>().Init(this, true);
        }

        private void InitializeGenome(Genome genome, Vector2 speedRange, Vector2 viscosityRange, Vector2 sizeRange, Vector2 hueRange, Vector2 emissionRange)
        {
            genome.speed = Random.Range(speedRange.x, speedRange.y);
            genome.viscosity = Random.Range(viscosityRange.x, viscosityRange.y);
            genome.size = Random.Range(sizeRange.x, sizeRange.y);
            genome.hue = Random.Range(hueRange.x, hueRange.y);
            genome.emission = Random.Range(emissionRange.x, emissionRange.y);
        }

        private Vector2 RandomPosition()
        {
            Vector2 offset = new(Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f));
            while (Mathf.Abs(offset.x) < ignoreAreaSize.x * 0.5f &&
                   Mathf.Abs(offset.y) < ignoreAreaSize.y * 0.5f)
            {
                offset = new Vector2(Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                    Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f));
            }
            return useLocalSpace
                ? (Vector2)transform.TransformPoint(offset)
                : (Vector2)transform.position + offset;
        }

        /// <summary>
        /// 個体の死亡を通知します。
        /// </summary>
        /// <param name="predator">死亡した個体がPredatorかどうか</param>
        public void NotifyDeath(bool predator)
        {
            if (predator) predatorCount = Mathf.Max(0, predatorCount - 1);
            else preyCount = Mathf.Max(0, preyCount - 1);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
            Gizmos.matrix = useLocalSpace ? transform.localToWorldMatrix : Matrix4x4.identity;
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, areaSize.y, 0));
            Gizmos.DrawWireCube(transform.position, new Vector3(ignoreAreaSize.x, ignoreAreaSize.y, 0));
            
        }
#endif
    }

    public class PopulationTracker : MonoBehaviour
    {
        private LifeSpawner spawner;
        private bool isPredator;

        /// <summary>
        /// PopulationTrackerを初期化し、スポナーと個体の種類を設定します。
        /// </summary>
        /// <param name="s">LifeSpawnerのインスタンス。</param>
        /// <param name="p">この個体がPredatorであるかどうか。</param>
        public void Init(LifeSpawner s, bool p)
        {
            spawner = s;
            isPredator = p;
        }

        private void OnDestroy()
        {
            if (spawner) spawner.NotifyDeath(isPredator);
        }
    }
}