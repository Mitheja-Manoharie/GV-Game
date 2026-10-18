using System;
using LabLockdown.AI.Facility;
using LabLockdown.AI.Hunter.Collections;

namespace LabLockdown.AI.Hunter
{
    /// <summary>
    /// How noise spreads through the level graph.
    ///
    /// Loudness is defined as the open-air audible radius in metres. An event at one node spreads along
    /// the graph edges and loses loudness on every edge it crosses:
    ///
    ///     loss(edge) = Alpha * edge.Cost  +  (door is not open ? ClosedDoorLoss : 0)
    ///     level(n)   = loudness - (smallest total loss from the source to n)
    ///
    /// A node hears the event only if level(n) is at least Threshold; otherwise its level is reported as 0.
    ///
    /// Because every loss is non-negative and additive, the loudest route is the route with the smallest total
    /// loss, and a max-first Dijkstra search finds it exactly. Closed, locked and blocked doors all muffle
    /// sound the same way: sound passes a locked door, a body cannot.
    /// Sound travels only along graph edges, never through walls (a stated limitation).
    /// </summary>
    public sealed class SoundModel
    {
        public const float DefaultAlpha = 1f;
        public const float DefaultClosedDoorLoss = 3f;
        public const float DefaultThreshold = 1f;

        public SoundModel(float alpha = DefaultAlpha, float closedDoorLoss = DefaultClosedDoorLoss, float threshold = DefaultThreshold)
        {
            if (alpha <= 0f || float.IsNaN(alpha)) throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be positive.");
            if (closedDoorLoss < 0f || float.IsNaN(closedDoorLoss)) throw new ArgumentOutOfRangeException(nameof(closedDoorLoss));
            if (threshold <= 0f || float.IsNaN(threshold)) throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be positive.");

            Alpha = alpha;
            ClosedDoorLoss = closedDoorLoss;
            Threshold = threshold;
        }

        /// <summary>Loudness lost per metre of walking distance.</summary>
        public float Alpha { get; }

        /// <summary>Extra loudness lost when the sound crosses a door that is not open.</summary>
        public float ClosedDoorLoss { get; }

        /// <summary>Quietest level that can still be heard.</summary>
        public float Threshold { get; }

        public float Loss(GraphEdge edge, IDoorStateProvider doors)
        {
            float loss = Alpha * edge.Cost;
            if (edge.IsDoor && doors.GetState(edge.DoorId) != DoorState.Open) loss += ClosedDoorLoss;
            return loss;
        }

        /// <summary>
        /// Loudness heard at every node for one event. Index = node id. Inaudible nodes report 0.
        /// </summary>
        public float[] Propagate(LevelGraph graph, int sourceNode, float loudness, IDoorStateProvider doors)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (doors == null) throw new ArgumentNullException(nameof(doors));
            if (sourceNode < 0 || sourceNode >= graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(sourceNode));
            if (loudness < 0f || float.IsNaN(loudness))
                throw new ArgumentOutOfRangeException(nameof(loudness));

            var level = new float[graph.NodeCount];
            if (loudness < Threshold) return level;

            var done = new bool[graph.NodeCount];
            var open = new MinHeap<int>();
            level[sourceNode] = loudness;
            open.Push(sourceNode, -loudness);   // loudest first = smallest negative level first

            while (open.TryPop(out int current))
            {
                if (done[current]) continue;
                done[current] = true;

                foreach (GraphEdge edge in graph.EdgesOf(current))
                {
                    int next = edge.Other(current);
                    if (done[next]) continue;

                    float remaining = level[current] - Loss(edge, doors);
                    if (remaining >= Threshold && remaining > level[next])
                    {
                        level[next] = remaining;
                        open.Push(next, -remaining);
                    }
                }
            }
            return level;
        }
    }
}
