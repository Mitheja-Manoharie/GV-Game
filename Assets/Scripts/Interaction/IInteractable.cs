using UnityEngine;

namespace GVGame.Interaction
{
    /// <summary>
    /// Anything the player can use with the Interact key (E): doors, switches, terminals.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short text for a HUD prompt, e.g. "Open door".</summary>
        string GetPrompt(Transform interactor);

        /// <summary>Called when the player presses Interact while looking at this object.</summary>
        void Interact(Transform interactor);
    }
}
