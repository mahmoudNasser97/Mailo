using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using UnityEngine;

public class NPCOptimizedTDE : MonoBehaviour
{
    private AIBrain _brain;

    [Tooltip("How often should this NPC think? (ms) Larger = better performance")]
    public float aiTickRate = 0.15f;

    private float _lastThinkTime = 0f;

    void Awake()
    {
        _brain = GetComponent<AIBrain>();
    }

    void Update()
    {
        if (Time.time >= _lastThinkTime + aiTickRate)
        {
            _brain.BrainActive = true;
            _lastThinkTime = Time.time;
        }
        else
        {
            _brain.BrainActive = false;
        }
    }
}
