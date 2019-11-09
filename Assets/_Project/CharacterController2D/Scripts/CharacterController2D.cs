#define DEBUG_CC2D_RAYS
using UnityEngine;
using System;
using System.Collections.Generic;
using TF.Colliders;
using TF.Core;
using FixedPointy;
using TF;

/// <summary>
/// Conversion of https://github.com/prime31/CharacterController2D.
/// </summary>
[RequireComponent( typeof( TFPolygonCollider ), typeof( TFRigidbody ) )]
public class CharacterController2D : MonoBehaviour
{
    #region internal types
    struct CharacterRaycastOrigins
    {
        public FixVec2 topLeft;
        public FixVec2 bottomRight;
        public FixVec2 bottomLeft;
    }

    public class CharacterCollisionState2D
    {
        public bool right;
        public bool left;
        public bool above;
        public bool below;
        public bool becameGroundedThisFrame;
        public bool wasGroundedLastFrame;
        public bool movingDownSlope;
        public Fix slopeAngle;


        public bool hasCollision()
        {
            return below || right || left || above;
        }


        public void reset()
        {
            right = left = above = below = becameGroundedThisFrame = movingDownSlope = false;
            slopeAngle = Fix.zero;
        }


        public override string ToString()
        {
            return string.Format("[CharacterCollisionState2D] r: {0}, l: {1}, a: {2}, b: {3}, movingDownSlope: {4}, angle: {5}, wasGroundedLastFrame: {6}, becameGroundedThisFrame: {7}",
                                 right, left, above, below, movingDownSlope, slopeAngle, wasGroundedLastFrame, becameGroundedThisFrame);
        }
    }

    #endregion

    #region events, properties and fields

    public event Action<TFRaycastHit2D> onControllerCollidedEvent;
    public event Action<TFCollider> onTriggerEnterEvent;
    public event Action<TFCollider> onTriggerStayEvent;
    public event Action<TFCollider> onTriggerExitEvent;


    /// <summary>
    /// when true, one way platforms will be ignored when moving vertically for a single frame
    /// </summary>
    public bool ignoreOneWayPlatformsThisFrame;

    //[Range(0.001f, 0.3f)] Fix _skinWidth = (Fix)0.02f;
    [SerializeField] Fix _skinWidth = (Fix)0.02f;

    /// <summary>
    /// defines how far in from the edges of the collider rays are cast from. If cast with a 0 extent it will often result in ray hits that are
    /// not desired (for example a foot collider casting horizontally from directly on the surface can result in a hit)
    /// </summary>
    public Fix skinWidth
    {
        get { return _skinWidth; }
        set
        {
            _skinWidth = value;
            //recalculateDistanceBetweenRays();
        }
    }


    /// <summary>
    /// mask with all layers that the player should interact with
    /// </summary>
    public LayerMask platformMask = 0;

    /// <summary>
    /// mask with all layers that trigger events should fire when intersected
    /// </summary>
    public LayerMask triggerMask = 0;

    /// <summary>
    /// mask with all layers that should act as one-way platforms. Note that one-way platforms should always be EdgeCollider2Ds. This is because it does not support being
    /// updated anytime outside of the inspector for now.
    /// </summary>
    [SerializeField] LayerMask oneWayPlatformMask = 0;

    /// <summary>
    /// the max slope angle that the CC2D can climb
    /// </summary>
    /// <value>The slope limit.</value>
    //[Range(0f, 90f)] public Fix slopeLimit = (Fix)30;
    public Fix slopeLimit = (Fix)30;

    /// <summary>
    /// the threshold in the change in vertical movement between frames that constitutes jumping
    /// </summary>
    /// <value>The jumping threshold.</value>
    public Fix jumpingThreshold = (Fix)0.07f;


    /// <summary>
    /// curve for multiplying speed based on slope (negative = down slope and positive = up slope)
    /// </summary>
    public AnimationCurve slopeSpeedMultiplier = new AnimationCurve(new Keyframe(-90f, 1.5f), new Keyframe(0f, 1f), new Keyframe(90f, 0f));

    [Range(2, 20)] public int totalHorizontalRays = 8;
    [Range(2, 20)] public int totalVerticalRays = 4;


    /// <summary>
    /// this is used to calculate the downward ray that is cast to check for slopes. We use the somewhat arbitrary value 75 degrees
    /// to calculate the length of the ray that checks for slopes.
    /// </summary>
    Fix _slopeLimitTangent = (Fix)Mathf.Tan(75f * Mathf.Deg2Rad);


    [HideInInspector] [NonSerialized] public TFTransform tfTransform;
    [HideInInspector] [NonSerialized] public TFPolygonCollider boxCollider;
    [HideInInspector] [NonSerialized] public TFRigidbody rigidBody2D;

    [HideInInspector] [NonSerialized] public CharacterCollisionState2D collisionState = new CharacterCollisionState2D();
    [HideInInspector] [NonSerialized] public FixVec2 velocity;
    public bool isGrounded { get { return collisionState.below; } }

    readonly Fix kSkinWidthFloatFudgeFactor = (Fix)0.001f;

    #endregion

    /// <summary>
    /// holder for our raycast origin corners (TR, TL, BR, BL)
    /// </summary>
    CharacterRaycastOrigins _raycastOrigins;

    /// <summary>
    /// stores our raycast hit during movement
    /// </summary>
    TFRaycastHit2D _raycastHit;

    /// <summary>
    /// stores any raycast hits that occur this frame. we have to store them in case we get a hit moving
    /// horizontally and vertically so that we can send the events after all collision state is set
    /// </summary>
    List<TFRaycastHit2D> _raycastHitsThisFrame = new List<TFRaycastHit2D>(2);

    // horizontal/vertical movement data
    Fix _verticalDistanceBetweenRays;
    Fix _horizontalDistanceBetweenRays;

    // we use this flag to mark the case where we are travelling up a slope and we modified our delta.y to allow the climb to occur.
    // the reason is so that if we reach the end of the slope we can make an adjustment to stay grounded
    bool _isGoingUpSlope = false;

    #region Monobehaviour
    void Awake()
    {
        // add our one-way platforms to our normal platform mask so that we can land on them from above
        platformMask |= oneWayPlatformMask;

        // cache some components
        tfTransform = GetComponent<TFTransform>();
        boxCollider = GetComponent<TFPolygonCollider>();
        rigidBody2D = GetComponent<TFRigidbody>();

        if (rigidBody2D)
        {
            rigidBody2D.OnTriggerEnter += TFOnTriggerEnter;
            rigidBody2D.OnTriggerStay += TFOnTriggerStay;
            rigidBody2D.OnTriggerExit += TFOnTriggerExit;
        }

        // here, we trigger our properties that have setters with bodies
        skinWidth = _skinWidth;

        // we want to set our CC2D to ignore all collision layers except what is in our triggerMask
        for (var i = 0; i < 32; i++)
        {
            // see if our triggerMask contains this layer and if not ignore it
            if ((triggerMask.value & 1 << i) == 0)
                Physics2D.IgnoreLayerCollision(gameObject.layer, i);
        }
    }

    public void TFOnTriggerEnter(TFCollider col)
    {
        if (onTriggerEnterEvent != null)
            onTriggerEnterEvent(col);
    }


    public void TFOnTriggerStay(TFCollider col)
    {
        if (onTriggerStayEvent != null)
            onTriggerStayEvent(col);
    }


    public void TFOnTriggerExit(TFCollider col)
    {
        if (onTriggerExitEvent != null)
            onTriggerExitEvent(col);
    }

    #endregion

    [System.Diagnostics.Conditional("DEBUG_CC2D_RAYS")]
    void DrawRay(Vector3 start, Vector3 dir, Color color)
    {
        Debug.DrawRay(start, dir, color);
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            var v3 = Input.mousePosition;
            v3.z = 0;
            v3 = Camera.main.ScreenToWorldPoint(v3);

            FixVec2 pointB = new FixVec2((Fix)v3.x, (Fix)v3.y);

            TFRaycastHit2D h = TFPhysics.Raycast((FixVec2)tfTransform.Position, (FixVec2)(pointB - tfTransform.Position).Normalized(), 5, platformMask);
        }
    }

    #region Public
    /// <summary>
    /// attempts to move the character to position + deltaMovement. Any colliders in the way will cause the movement to
    /// stop when run into.
    /// </summary>
    /// <param name="deltaMovement">Delta movement.</param>
    public void move(FixVec2 deltaMovement)
    {
        // save off our current grounded state which we will use for wasGroundedLastFrame and becameGroundedThisFrame
        collisionState.wasGroundedLastFrame = collisionState.below;

        // clear our state
        collisionState.reset();
        _raycastHitsThisFrame.Clear();
        _isGoingUpSlope = false;

        primeRaycastOrigins();


        // first, we check for a slope below us before moving
        // only check slopes if we are going down and grounded
        if (deltaMovement.y < Fix.zero && collisionState.wasGroundedLastFrame)
            handleVerticalSlope(ref deltaMovement);

        // now we check movement in the horizontal dir
        if (deltaMovement.x != Fix.zero)
            moveHorizontally(ref deltaMovement);

        // next, check movement in the vertical dir
        if (deltaMovement.y != Fix.zero)
            moveVertically(ref deltaMovement);

        // move then update our state
        tfTransform.Position += deltaMovement;

        // only calculate velocity if we have a non-zero deltaTime
        if (Time.deltaTime > 0f)
            velocity = deltaMovement / (Fix)Time.deltaTime;

        // set our becameGrounded state based on the previous and current collision state
        if (!collisionState.wasGroundedLastFrame && collisionState.below)
            collisionState.becameGroundedThisFrame = true;

        // if we are going up a slope we artificially set a y velocity so we need to zero it out here
        if (_isGoingUpSlope)
            velocity.y = 0;

        // send off the collision events if we have a listener
        if (onControllerCollidedEvent != null)
        {
            for (var i = 0; i < _raycastHitsThisFrame.Count; i++)
                onControllerCollidedEvent(_raycastHitsThisFrame[i]);
        }

        ignoreOneWayPlatformsThisFrame = false;
    }

    /// <summary>
    /// moves directly down until grounded
    /// </summary>
    public void warpToGrounded()
    {
        do
        {
            move(new FixVec2(0, -1));
        } while (!isGrounded);
    }

    /// <summary>
    /// this should be called anytime you have to modify the BoxCollider2D at runtime. It will recalculate the distance between the rays used for collision detection.
    /// It is also used in the skinWidth setter in case it is changed at runtime.
    /// </summary>
    public void recalculateDistanceBetweenRays()
    {
        FixVec2 size = boxCollider.Size;
        // figure out the distance between our rays in both directions
        // horizontal
        var colliderUseableHeight = size.y * FixMath.Abs(tfTransform.LocalScale.y) - (2 * _skinWidth);
        _verticalDistanceBetweenRays = colliderUseableHeight / (totalHorizontalRays - Fix.one);

        // vertical
        var colliderUseableWidth = size.x * FixMath.Abs(tfTransform.LocalScale.x) - (2 * _skinWidth);
        _horizontalDistanceBetweenRays = colliderUseableWidth / (totalVerticalRays - Fix.one);
    }
    #endregion

    #region Movement Methods
    /// <summary>
    /// resets the raycastOrigins to the current extents of the box collider inset by the skinWidth. It is inset
    /// to avoid casting a ray from a position directly touching another collider which results in wonky normal data.
    /// </summary>
    void primeRaycastOrigins()
    {
        // our raycasts need to be fired from the bounds inset by the skinWidth
        AABB modifiedBounds = boxCollider.boundingBox;
        modifiedBounds.Expand(-2 * _skinWidth);

        _raycastOrigins.topLeft = new FixVec2(modifiedBounds.min.x, modifiedBounds.max.y);
        _raycastOrigins.bottomRight = new FixVec2(modifiedBounds.max.x, modifiedBounds.min.y);
        _raycastOrigins.bottomLeft = modifiedBounds.min;
    }

    /// <summary>
    /// we have to use a bit of trickery in this one. The rays must be cast from a small distance inside of our
    /// collider (skinWidth) to avoid zero distance rays which will get the wrong normal. Because of this small offset
    /// we have to increase the ray distance skinWidth then remember to remove skinWidth from deltaMovement before
    /// actually moving the player
    /// </summary>
    void moveHorizontally(ref FixVec2 deltaMovement)
    {
        var isGoingRight = deltaMovement.x > 0;
        var rayDistance = FixMath.Abs(deltaMovement.x) + _skinWidth;
        var rayDirection = isGoingRight ? FixVec2.right : -FixVec2.right;
        var initialRayOrigin = isGoingRight ? _raycastOrigins.bottomRight : _raycastOrigins.bottomLeft;

        for (var i = 0; i < totalHorizontalRays; i++)
        {
            var ray = new FixVec2(initialRayOrigin.x, initialRayOrigin.y + i * _verticalDistanceBetweenRays);

            // if we are grounded we will include oneWayPlatforms only on the first ray (the bottom one). this will allow us to
            // walk up sloped oneWayPlatforms.
            if(i == 0 && collisionState.wasGroundedLastFrame)
            {
                _raycastHit = TFPhysics.Raycast(ray, rayDirection, rayDistance, platformMask);
            }
            else
            {
                _raycastHit = TFPhysics.Raycast(ray, rayDirection, rayDistance, platformMask & ~oneWayPlatformMask);
            }

            if (_raycastHit)
            {
                // the bottom ray can hit a slope but no other ray can so we have special handling for these cases
                if(i == 0 && handleHorizontalSlope(ref deltaMovement, FixVec2.Angle(_raycastHit.normal, FixVec2.up))){
                    _raycastHitsThisFrame.Add(_raycastHit);
                    // if we weren't grounded last frame, that means we're landing on a slope horizontally.
                    // this ensures that we stay flush to that slope
                    if (!collisionState.wasGroundedLastFrame)
                    {
                        Fix flushDistance = FixMath.Sign(deltaMovement.x) * (_raycastHit.distance - skinWidth);
                        tfTransform.Position += new FixVec2(flushDistance, 0);
                    }
                    break;
                }

                // set our new deltaMovement and recalculate the rayDistance taking it into account
                deltaMovement.x = _raycastHit.point.x - ray.x;
                rayDistance = FixMath.Abs(deltaMovement.x);

                // remember to remove the skinWidth from our deltaMovement
                if (isGoingRight)
                {
                    deltaMovement.x -= _skinWidth;
                    collisionState.right = true;
                }
                else
                {
                    deltaMovement.x += _skinWidth;
                    collisionState.left = true;
                }

                _raycastHitsThisFrame.Add(_raycastHit);

                // we add a small fudge factor for the float operations here. if our rayDistance is smaller
                // than the width + fudge bail out because we have a direct impact
                if (rayDistance < _skinWidth + kSkinWidthFloatFudgeFactor)
                    break;
            }
        }
    }

    /// <summary>
    /// handles adjusting deltaMovement if we are going up a slope.
    /// </summary>
    /// <returns><c>true</c>, if horizontal slope was handled, <c>false</c> otherwise.</returns>
    /// <param name="deltaMovement">Delta movement.</param>
    /// <param name="angle">Angle.</param>
    bool handleHorizontalSlope(ref FixVec2 deltaMovement, Fix angle)
    {
        // disregard 90 degree angles (walls)
        if (FixMath.Round(angle) == 90)
            return false;

        if (angle < slopeLimit)
        {
            // we only need to adjust the deltaMovement if we are not jumping
            // TODO: this uses a magic number which isn't ideal! The alternative is to have the user pass in if there is a jump this frame
            if (deltaMovement.y < jumpingThreshold)
            {
                // apply the slopeModifier to slow our movement up the slope
                Fix slopeModifier = (Fix)slopeSpeedMultiplier.Evaluate((float)angle);
                deltaMovement.x *= slopeModifier;

                // we dont set collisions on the sides for this since a slope is not technically a side collision.
                // smooth y movement when we climb. we make the y movement equivalent to the actual y location that corresponds
                // to our new x location using our good friend Pythagoras
                deltaMovement.y = FixMath.Abs(FixMath.Tan(angle * FixMath.Deg2Rad) * deltaMovement.x);
                var isGoingRight = deltaMovement.x > 0;

                // safety check. we fire a ray in the direction of movement just in case the diagonal we calculated above ends up
                // going through a wall. if the ray hits, we back off the horizontal movement to stay in bounds.
                var ray = isGoingRight ? _raycastOrigins.bottomRight : _raycastOrigins.bottomLeft;
                TFRaycastHit2D raycastHit;
                if (collisionState.wasGroundedLastFrame)
                {
                    raycastHit = TFPhysics.Raycast((FixVec2)ray, (FixVec2)deltaMovement.Normalized(), deltaMovement.GetMagnitude(), platformMask);
                }
                else
                {
                    raycastHit = TFPhysics.Raycast((FixVec2)ray, (FixVec2)deltaMovement.Normalized(), deltaMovement.GetMagnitude(), platformMask & ~oneWayPlatformMask);
                }

                if (raycastHit)
                {
                    // we crossed an edge when using Pythagoras calculation, so we set the actual delta movement to the ray hit location
                    deltaMovement = raycastHit.point - ray;
                    if (isGoingRight)
                        deltaMovement.x -= _skinWidth;
                    else
                        deltaMovement.x += _skinWidth;
                }

                _isGoingUpSlope = true;
                collisionState.below = true;
                collisionState.slopeAngle = -angle;
            }
        }
        else // too steep. get out of here
        {
            deltaMovement.x = Fix.zero;
        }

        return true;
    }

    void moveVertically(ref FixVec2 deltaMovement)
    {
        var isGoingUp = deltaMovement.y > 0;
        var rayDistance = FixMath.Abs(deltaMovement.y) + _skinWidth;
        var rayDirection = isGoingUp ? FixVec2.up : -FixVec2.up;
        var initialRayOrigin = isGoingUp ? _raycastOrigins.topLeft : _raycastOrigins.bottomLeft;

        // apply our horizontal deltaMovement here so that we do our raycast from the actual position we would be in if we had moved
        initialRayOrigin.x += deltaMovement.x;

        // if we are moving up, we should ignore the layers in oneWayPlatformMask
        var mask = platformMask;
        if ((isGoingUp && !collisionState.wasGroundedLastFrame) || ignoreOneWayPlatformsThisFrame)
        {
            mask &= ~oneWayPlatformMask;
        }

        for (var i = 0; i < totalVerticalRays; i++)
        {
            var ray = new FixVec2(initialRayOrigin.x + i * _horizontalDistanceBetweenRays, initialRayOrigin.y);

            _raycastHit = TFPhysics.Raycast(ray, rayDirection, rayDistance, mask);
            if (_raycastHit)
            {
                // set our new deltaMovement and recalculate the rayDistance taking it into account
                deltaMovement.y = _raycastHit.point.y - ray.y;
                rayDistance = FixMath.Abs(deltaMovement.y);

                // remember to remove the skinWidth from our deltaMovement
                if (isGoingUp)
                {
                    deltaMovement.y -= _skinWidth;
                    collisionState.above = true;
                }
                else
                {
                    deltaMovement.y += _skinWidth;
                    collisionState.below = true;
                }

                _raycastHitsThisFrame.Add(_raycastHit);

                // this is a hack to deal with the top of slopes. if we walk up a slope and reach the apex we can get in a situation
                // where our ray gets a hit that is less then skinWidth causing us to be ungrounded the next frame due to residual velocity.
                if (!isGoingUp && deltaMovement.y > Fix.zero)
                {
                    _isGoingUpSlope = true;
                }

                // we add a small fudge factor for the float operations here. if our rayDistance is smaller
                // than the width + fudge bail out because we have a direct impact
                if (rayDistance < _skinWidth + kSkinWidthFloatFudgeFactor)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// checks the center point under the BoxCollider2D for a slope. If it finds one then the deltaMovement is adjusted so that
    /// the player stays grounded and the slopeSpeedModifier is taken into account to speed up movement.
    /// </summary>
    /// <param name="deltaMovement">Delta movement.</param>
    private void handleVerticalSlope(ref FixVec2 deltaMovement)
    {
        // slope check from the center of our collider
        Fix centerOfCollider = (_raycastOrigins.bottomLeft.x + _raycastOrigins.bottomRight.x) * (Fix.one / 2);
        FixVec2 rayDirection = -FixVec2.up;

        // the ray distance is based on our slopeLimit
        Fix slopeCheckRayDistance = _slopeLimitTangent * (_raycastOrigins.bottomRight.x - centerOfCollider);

        FixVec2 slopeRay = new FixVec2(centerOfCollider, _raycastOrigins.bottomLeft.y);
        _raycastHit = TFPhysics.Raycast(slopeRay, rayDirection, slopeCheckRayDistance, platformMask);
        if (_raycastHit)
        {
            // bail out if we have no slope
            var angle = FixVec2.Angle(_raycastHit.normal, FixVec2.up);
            if (angle == 0)
            {
                return;
            }

            // we are moving down the slope if our normal and movement direction are in the same x direction
            var isMovingDownSlope = FixMath.Sign(_raycastHit.normal.x) == FixMath.Sign(deltaMovement.x);
            if (isMovingDownSlope)
            {
                // going down we want to speed up in most cases so the slopeSpeedMultiplier curve should be > 1 for negative angles
                Fix slopeModifier = (Fix)slopeSpeedMultiplier.Evaluate((float)-angle);
                // we add the extra downward movement here to ensure we "stick" to the surface below
                deltaMovement.y += _raycastHit.point.y - slopeRay.y - skinWidth;
                deltaMovement = new FixVec2(deltaMovement.x, deltaMovement.y)
                    + new FixVec2(deltaMovement.x * slopeModifier * (1 - (angle/90)), (deltaMovement.x * slopeModifier * (angle/90)));
                collisionState.movingDownSlope = true;
                collisionState.slopeAngle = angle;
            }
        }
    }
    #endregion
}
