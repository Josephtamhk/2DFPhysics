using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TF
{
    public struct TFLayerMask
    {
        private int mask;

        public int value
        {
            get
            {
                return mask;
            }
            set
            {
                mask = value;
            }
        }

        public static implicit operator int(TFLayerMask mask)
        {
            return mask.mask;
        }

        public static implicit operator TFLayerMask(int intVal)
        {
            TFLayerMask layerMask;
            layerMask.mask = intVal;
            return layerMask;
        }
    }
}
