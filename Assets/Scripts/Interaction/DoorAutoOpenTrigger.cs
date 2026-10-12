using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Interaction
{
    /// <summary>
    /// Trigger volume placed in front of a door. Opens the door when an AI agent (any NavMeshAgent)
    /// walks into it. It only closes doors IT opened, after the last agent has left, so it never
    /// fights with a door the player opened by hand.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DoorAutoOpenTrigger : MonoBehaviour
    {
        public DoorController door;
        [Tooltip("Close the door again after the last agent leaves (only if an agent opened it).")]
        public bool closeWhenEmpty = true;
        public float closeDelay = 1.5f;

        readonly HashSet<NavMeshAgent> _inside = new HashSet<NavMeshAgent>();
        bool _openedByAgent;
        float _emptyTimer;

        void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
            door = GetComponentInParent<DoorController>();
        }

        void OnTriggerEnter(Collider other)
        {
            var agent = other.GetComponentInParent<NavMeshAgent>();
            if (agent == null || door == null || door.IsLocked) return;
            _inside.Add(agent);

            bool wasShut = door.Current == DoorController.State.Closed || door.Current == DoorController.State.Closing;
            door.Open(agent.transform);
            if (wasShut) _openedByAgent = true;
        }

        void OnTriggerExit(Collider other)
        {
            var agent = other.GetComponentInParent<NavMeshAgent>();
            if (agent != null) _inside.Remove(agent);
        }

        void Update()
        {
            if (!closeWhenEmpty || door == null || !_openedByAgent) return;

            // Someone (the player) closed it already -> nothing left to manage.
            if (door.Current == DoorController.State.Closed || door.Current == DoorController.State.Closing)
            {
                _openedByAgent = false;
                _emptyTimer = 0f;
                return;
            }

            _inside.RemoveWhere(a => a == null || !a.gameObject.activeInHierarchy);
            if (_inside.Count > 0) { _emptyTimer = 0f; return; }

            _emptyTimer += Time.deltaTime;
            if (_emptyTimer >= closeDelay)
            {
                door.Close();
                _openedByAgent = false;
                _emptyTimer = 0f;
            }
        }
    }
}