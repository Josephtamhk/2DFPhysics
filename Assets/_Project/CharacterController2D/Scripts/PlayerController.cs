using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TF.Core;
using FixedPointy;

public class PlayerController : MonoBehaviour
{
    public TFRigidbody rb;
    public CharacterController2D charController;

    public Fix movementSpeed = (Fix)0.3f;

    private void Update()
    {
        if (Input.GetKey(KeyCode.A))
        {
            charController.move(FixVec2.right * -movementSpeed);
        } else if (Input.GetKey(KeyCode.D))
        {
            charController.move(FixVec2.right * movementSpeed);
        }

        if (Input.GetKey(KeyCode.W))
        {
            charController.move(FixVec2.up * movementSpeed);
        }else if (Input.GetKey(KeyCode.S))
        {
            charController.move(FixVec2.up * -movementSpeed);
        }
    }
}
