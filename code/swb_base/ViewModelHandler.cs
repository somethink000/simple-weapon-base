﻿using SWB.Shared;
using System;

namespace SWB.Base;

public class ViewModelHandler : Component
{
	public ModelRenderer ViewModelRenderer { get; set; }
	public SkinnedModelRenderer ViewModelHandsRenderer { get; set; }
	public Weapon Weapon { get; set; }
	public CameraComponent Camera { get; set; }
	IPlayerBase player => Weapon.Owner;

	float animSpeed = 1;
	float playerFOVSpeed = 1;

	// Target animation values
	Vector3 targetVectorPos;
	Vector3 targetVectorRot;
	float targetPlayerFOV = -1;
	float targetWeaponFOV = -1;

	// Finalized animation values
	Vector3 finalVectorPos;
	Vector3 finalVectorRot;
	float finalPlayerFOV;
	float finalWeaponFOV;

	// Sway
	Rotation lastEyeRot;

	// Jumping Animation
	float jumpTime;
	float landTime;

	// Aim animation
	float aimTime;

	// Helpful values
	Vector3 localVel;

	protected override void OnStart()
	{
		// Replication bug?
		if ( IsProxy )
		{
			this.GameObject.Destroy();
			return;
		}
	}

	protected override void OnUpdate()
	{
		ViewModelRenderer.Enabled = player.IsFirstPerson;
		ViewModelHandsRenderer.Enabled = player.IsFirstPerson;

		if ( !player.IsFirstPerson ) return;

		// For particles & lighting
		Camera.Transform.Position = Scene.Camera.Transform.Position;
		Camera.Transform.Rotation = Scene.Camera.Transform.Rotation;

		if ( targetWeaponFOV == -1 )
		{
			//finalPlayerFOV = Game.Preferences.FieldOfView;
			finalPlayerFOV = 90;
			targetWeaponFOV = Weapon.FOV;
			finalWeaponFOV = Weapon.FOV;
		}

		Transform.Position = Camera.Transform.Position;
		Transform.Rotation = Camera.Transform.Rotation;

		// Smoothly transition the vectors with the target values
		finalVectorPos = finalVectorPos.LerpTo( targetVectorPos, animSpeed * RealTime.Delta );
		finalVectorRot = finalVectorRot.LerpTo( targetVectorRot, animSpeed * RealTime.Delta );
		finalPlayerFOV = MathX.LerpTo( finalPlayerFOV, targetPlayerFOV, playerFOVSpeed * animSpeed * RealTime.Delta );
		finalWeaponFOV = MathX.LerpTo( finalWeaponFOV, targetWeaponFOV, playerFOVSpeed * animSpeed * RealTime.Delta );
		animSpeed = 10 * Weapon.AnimSpeed;

		// Change the angles and positions of the viewmodel with the new vectors
		Transform.Position += finalVectorPos.z * Transform.Rotation.Up + finalVectorPos.y * Transform.Rotation.Forward + finalVectorPos.x * Transform.Rotation.Right;
		Transform.Rotation *= Rotation.From( finalVectorRot.x, finalVectorRot.y, finalVectorRot.z );
		Camera.FieldOfView = Screen.CreateVerticalFieldOfView( finalWeaponFOV );
		player.Camera.FieldOfView = Screen.CreateVerticalFieldOfView( finalPlayerFOV );

		// Initialize the target vectors for this frame
		targetVectorPos = Vector3.Zero;
		targetVectorRot = Vector3.Zero;
		// targetPlayerFOV = Game.Preferences.FieldOfView;
		targetPlayerFOV = 90;
		targetWeaponFOV = Weapon.FOV;

		// I'm sure there's something already that does this for me, but I spend an hour
		// searching through the wiki and a bunch of other garbage and couldn't find anything...
		// So I'm doing it manually. Problem solved.
		var eyeRot = player.EyeAngles.ToRotation();
		localVel = new Vector3( eyeRot.Right.Dot( player.Velocity ), eyeRot.Forward.Dot( player.Velocity ), player.Velocity.z );

		HandleIdleAnimation();
		HandleWalkAnimation();
		HandleSwayAnimation();
		HandleIronAnimation();
		HandleSprintAnimation();
		HandleJumpAnimation();
	}

	void HandleIdleAnimation()
	{
		// No swaying if aiming
		if ( Weapon.IsAiming )
			return;

		// Perform a "breathing" animation
		float breatheTime = RealTime.Now * 2.0f;
		targetVectorPos -= new Vector3( MathF.Cos( breatheTime / 4.0f ) / 8.0f, 0.0f, -MathF.Cos( breatheTime / 4.0f ) / 32.0f );
		targetVectorRot -= new Vector3( MathF.Cos( breatheTime / 5.0f ), MathF.Cos( breatheTime / 4.0f ), MathF.Cos( breatheTime / 7.0f ) );

		// Crouching animation
		if ( Input.Down( InputButtonHelper.Duck ) && player.IsOnGround )
			targetVectorPos += new Vector3( -1.0f, -1.0f, 0.5f );
	}


	void HandleWalkAnimation()
	{
		float breatheTime = RealTime.Now * 16.0f;
		float walkSpeed = new Vector3( player.Velocity.x, player.Velocity.y, 0.0f ).Length;
		float maxWalkSpeed = 200.0f;
		float roll = 0.0f;
		float yaw = 0.0f;

		// Check if on the ground
		if ( !player.IsOnGround )
			return;

		// Check if sprinting
		if ( player.IsRunning )
		{
			breatheTime = RealTime.Now * 18.0f;
			maxWalkSpeed = 100.0f;
		}

		// Check for sideways velocity to sway the gun slightly
		if ( Weapon.IsAiming || localVel.x > 0.0f )
			roll = -7.0f * (localVel.x / maxWalkSpeed);
		else if ( localVel.x < 0.0f )
			yaw = 3.0f * (localVel.x / maxWalkSpeed);

		// Check if ADS & firing
		if ( Weapon.IsAiming && Weapon.TimeSincePrimaryShoot < 0.1f )
		{
			targetVectorRot -= new Vector3( 0, 0, roll );
			return;
		}

		// Perform walk cycle
		targetVectorPos -= new Vector3( (-MathF.Cos( breatheTime / 2.0f ) / 5.0f) * walkSpeed / maxWalkSpeed - yaw / 4.0f, 0.0f, 0.0f );
		targetVectorRot -= new Vector3( (Math.Clamp( MathF.Cos( breatheTime ), -0.3f, 0.3f ) * 2.0f) * walkSpeed / maxWalkSpeed, (-MathF.Cos( breatheTime / 2.0f ) * 1.2f) * walkSpeed / maxWalkSpeed - yaw * 1.5f, roll );
	}


	void HandleSwayAnimation()
	{
		int swayspeed = 5;

		// Fix the sway faster if we're ironsighting
		if ( Weapon.IsAiming )
			swayspeed = 20;

		// Lerp the eye position
		lastEyeRot = Rotation.Lerp( lastEyeRot, player.Camera.Transform.Rotation, swayspeed * RealTime.Delta );

		// Calculate the difference between our current eye angles and old (lerped) eye angles
		Angles angDif = player.Camera.Transform.Rotation.Angles() - lastEyeRot.Angles();
		angDif = new Angles( angDif.pitch, MathX.RadianToDegree( MathF.Atan2( MathF.Sin( MathX.DegreeToRadian( angDif.yaw ) ), MathF.Cos( MathX.DegreeToRadian( angDif.yaw ) ) ) ), 0 );

		// Perform sway
		targetVectorPos += new Vector3( Math.Clamp( angDif.yaw * 0.04f, -1.5f, 1.5f ), 0.0f, Math.Clamp( angDif.pitch * 0.04f, -1.5f, 1.5f ) );
		targetVectorRot += new Vector3( Math.Clamp( angDif.pitch * 0.2f, -4.0f, 4.0f ), Math.Clamp( angDif.yaw * 0.2f, -4.0f, 4.0f ), 0.0f );
	}


	void HandleIronAnimation()
	{
		if ( Weapon.IsAiming )
		{
			float speedMod = 1;
			if ( aimTime == 0 )
			{
				aimTime = RealTime.Now;
			}

			// var timeDiff = RealTime.Now - aimTime;

			// Mod only while actively scoping
			//if ( Weapon.IsScoped || (!Weapon.IsScoped && timeDiff < 0.2f) )
			//{
			//	speedMod = timeDiff * 10;
			//}

			animSpeed = 10 * Weapon.AnimSpeed * speedMod;
			targetVectorPos += Weapon.AimAnimData.Pos;
			targetVectorRot += MathUtil.ToVector3( Weapon.AimAnimData.Angle );

			if ( Weapon.AimPlayerFOV > 0 )
				targetPlayerFOV = Weapon.AimPlayerFOV;

			//if ( Weapon.General.ScopedPlayerFOV > 0 && Weapon.IsScoped )
			//	targetPlayerFOV = Weapon.General.ScopedPlayerFOV;

			if ( Weapon.AimFOV > 0 )
				targetWeaponFOV = Weapon.AimFOV;

			playerFOVSpeed = Weapon.AimInFOVSpeed;
		}
		else
		{
			aimTime = 0;
			targetWeaponFOV = Weapon.FOV;

			if ( finalPlayerFOV != Weapon.AimPlayerFOV )
			{
				playerFOVSpeed = Weapon.AimOutFOVSpeed;
			}
		}
	}

	void HandleSprintAnimation()
	{
		if ( Weapon.IsRunning && Weapon.RunAnimData != AngPos.Zero /* && !Weapon.IsCustomizing */ )
		{
			targetVectorPos += Weapon.RunAnimData.Pos;
			targetVectorRot += MathUtil.ToVector3( Weapon.RunAnimData.Angle );
		}
	}

	/*
void HandleCustomizeAnimation()
{
if ( Weapon.IsCustomizing && Weapon.CustomizeAnimData != AngPos.Zero )
{
targetVectorPos += Weapon.CustomizeAnimData.Pos;
targetVectorRot += MathUtil.ToVector3( Weapon.CustomizeAnimData.Angle );
}
}

	*/

	void HandleJumpAnimation()
	{
		// If we're not on the ground, reset the landing animation time
		if ( !player.IsOnGround )
			landTime = RealTime.Now + 0.31f;

		// Reset the timers once they elapse
		if ( landTime < RealTime.Now && landTime != 0.0f )
		{
			landTime = 0.0f;
			jumpTime = 0.0f;
		}

		// If we jumped, start the animation
		if ( Input.Down( InputButtonHelper.Jump ) && jumpTime == 0.0f )
		{
			jumpTime = RealTime.Now + 0.31f;
			landTime = 0.0f;
		}

		// If we're not ironsighting, do a fancy jump animation
		if ( !Weapon.IsAiming )
		{
			if ( jumpTime > RealTime.Now )
			{
				// If we jumped, do a curve upwards
				float f = 0.31f - (jumpTime - RealTime.Now);
				float xx = MathUtil.BezierY( f, 0.0f, -4.0f, 0.0f );
				float yy = 0.0f;
				float zz = MathUtil.BezierY( f, 0.0f, -2.0f, -5.0f );
				float pt = MathUtil.BezierY( f, 0.0f, -4.36f, 10.0f );
				float yw = xx;
				float rl = MathUtil.BezierY( f, 0.0f, -10.82f, -5.0f );
				targetVectorPos += new Vector3( xx, yy, zz ) / 4.0f;
				targetVectorRot += new Vector3( pt, yw, rl ) / 4.0f;
				animSpeed = 20.0f;
			}
			else if ( !player.IsOnGround )
			{
				// Shaking while falling
				float breatheTime = RealTime.Now * 30.0f;
				targetVectorPos += new Vector3( MathF.Cos( breatheTime / 2.0f ) / 16.0f, 0.0f, -5.0f + (MathF.Sin( breatheTime / 3.0f ) / 16.0f) ) / 4.0f;
				targetVectorRot += new Vector3( 10.0f - (MathF.Sin( breatheTime / 3.0f ) / 4.0f), MathF.Cos( breatheTime / 2.0f ) / 4.0f, -5.0f ) / 4.0f;
				animSpeed = 20.0f;
			}
			else if ( landTime > RealTime.Now )
			{
				// If we landed, do a fancy curve downwards
				float f = landTime - RealTime.Now;
				float xx = MathUtil.BezierY( f, 0.0f, -4.0f, 0.0f );
				float yy = 0.0f;
				float zz = MathUtil.BezierY( f, 0.0f, -2.0f, -5.0f );
				float pt = MathUtil.BezierY( f, 0.0f, -4.36f, 10.0f );
				float yw = xx;
				float rl = MathUtil.BezierY( f, 0.0f, -10.82f, -5.0f );
				targetVectorPos += new Vector3( xx, yy, zz ) / 2.0f;
				targetVectorRot += new Vector3( pt, yw, rl ) / 2.0f;
				animSpeed = 20.0f;
			}
		}
		else
			targetVectorPos += new Vector3( 0.0f, 0.0f, Math.Clamp( localVel.z / 1000.0f, -1.0f, 1.0f ) );
	}

}
