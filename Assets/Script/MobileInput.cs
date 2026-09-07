using UnityEngine;

using StarterAssets;

namespace MisaVondoFPSMobile
{
    public class MobileInput : MonoBehaviour
    {
        public FixedTouchField TouchField;
        [Min(0f)] public float touchSensitivity = 0.15f;
        public bool invertY = true;

        private StarterAssetsInputs _starterAssetsInputs;
        private bool _touchWasActive;

        private void Awake()
        {
            _starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        }

        private void Update()
        {
            if (TouchField == null || _starterAssetsInputs == null)
            {
                return;
            }

            bool touchIsActive = TouchField.Pressed;
            if (touchIsActive || _touchWasActive)
            {
                Vector2 lookDelta = TouchField.ConsumeDelta() * touchSensitivity;
                if (invertY)
                {
                    lookDelta.y = -lookDelta.y;
                }

                _starterAssetsInputs.LookDeltaInput(lookDelta);
            }

            _touchWasActive = touchIsActive;
        }

        private void OnDisable()
        {
            if (_starterAssetsInputs != null && _touchWasActive)
            {
                _starterAssetsInputs.LookDeltaInput(Vector2.zero);
            }

            _touchWasActive = false;
        }
    }
}

