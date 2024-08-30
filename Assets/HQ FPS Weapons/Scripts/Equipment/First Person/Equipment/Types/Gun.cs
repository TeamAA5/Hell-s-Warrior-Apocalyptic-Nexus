﻿using UnityEngine;
using Random = UnityEngine.Random;
using System.Linq;
using System;
using System.Data.Common;

namespace HQFPSWeapons
{
	public class Gun : ProjectileBasedWeapon
	{
		[BHeader("GUN SETTINGS", true)]

		public float damage;
		public GameObject bloodFX;
		[SerializeField]
		[Group]
		private GunSettings.Shooting m_Shooting = null;

		//Fire mode
		private float m_NextTimeCanChangeMode = -1f;

		// Cache some properties of the item
		protected ItemProperty.Value m_FireModes;


		public override void Wield(SaveableItem correspondingItem)
		{
			base.Wield(correspondingItem);

			//Select the firemode that the corresponding weapon is set on
			if (m_EHandler.CurrentlyAttachedItem.HasProperty("FireMode"))
			{
				m_FireModes = correspondingItem.GetProperty("FireMode");
				SelectedFireMode = (int)m_FireModes.Val.Current;

				SelectFireMode(SelectedFireMode);
			}
		}

		//Try change the fire mode
		public override bool ChangeFireMode()
		{
			if (m_EHandler.CurrentlyAttachedItem.HasProperty("FireMode") && Time.time > m_NextTimeCanChangeMode)
			{
				m_NextTimeCanChangeMode = Time.time + 0.3f;
				m_NextTimeCanUse = Time.time + 0.3f;

				int nextFireMode = GetNextFireModeIndex(SelectedFireMode);

				if (nextFireMode == SelectedFireMode)
					return false;
				else
					SelectedFireMode = nextFireMode;

				SelectFireMode(SelectedFireMode);

				//Play Audio & Procedural animation
				m_EHandler.PlaySounds(m_GunAudio.FireModeChangeSounds);

				return true;
			}

			return false;
		}

		public override void OnAimStart()
		{
			base.OnAimStart();

			nextTimeCanAim = Time.time + m_Shooting.AimThreeshold;
		}

		public override void OnUseEnd()
		{
			//Play fire tail sound
			if (m_GunAudio.FireTailSounds != null)
				m_EHandler.EquipmentManager.PlayPersistentAudio(m_GunAudio.FireTailSounds, GlobalVolumeManager.Instance.GetSoundVol(), ItemSelection.Method.RandomExcludeLast);
		}

		public override void Shoot(Camera camera)
		{
			base.Shoot(camera);

			// Shell drop sounds
			if (Player.IsGrounded.Get() == true && m_GunAudio.ShellDropSounds.Length > 0)
				m_EHandler.PlayDelayedSound(m_GunAudio.ShellDropSounds[Random.Range(0, m_GunAudio.ShellDropSounds.Length)]);

			//Raycast Shooting
			for (int i = 0; i < m_Shooting.RayCount; i++)
				DoHitscan(camera);
		}

		private void SelectFireMode(int selectedMode) 
		{
			if ((int)FireMode.Burst == selectedMode)
			{
				m_UseThreshold = fireMode.BurstDuration + fireMode.BurstPause;
				m_UseClickBuffer = true;
			}
			else if ((int)FireMode.FullAuto == selectedMode)
			{
				m_UseThreshold = 60f / fireMode.RoundsPerMinute;
				m_UseClickBuffer = true;
			}
			else if ((int)FireMode.SemiAuto == selectedMode)
			{
				m_UseThreshold = fireMode.FireDuration;
				m_UseClickBuffer = true;
			}
			else if ((int)FireMode.Safety == selectedMode)
			{
				m_UseThreshold = fireMode.FireDuration;
				m_UseClickBuffer = false;
			}

			//Set the firemode to the coressponding saveable item
			var fireModeRange = m_FireModes.Val;
			fireModeRange.Current = selectedMode;

			m_FireModes.SetValue(fireModeRange);
		}

		private void DoHitscan(Camera camera)
		{
			float spread = m_Shooting.SpreadOverTime.Evaluate(continuouslyUsedTimes / (float)m_Ammo.Settings.MagazineSize);
			
			if (Player.Jump.Active)
				spread *= m_Shooting.JumpSpreadFactor;
			else if (Player.Run.Active)
				spread *= m_Shooting.RunSpreadFactor;
			else if (Player.Crouch.Active)
				spread *= m_Shooting.CrouchSpreadFactor;
			else if (Player.Walk.Active)
				spread *= m_Shooting.WalkSpreadFactor;

			if (Player.Aim.Active)
				spread *= m_Shooting.AimSpreadFactor;

			RaycastHit hitInfo;

			Ray ray = camera.ViewportPointToRay(Vector2.one * 0.5f);
			Vector3 spreadVector = camera.transform.TransformVector(new Vector3(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f));
			ray.direction = Quaternion.Euler(spreadVector) * ray.direction;

			if (Physics.Raycast(ray, out hitInfo, m_Shooting.MaxDistance, m_Shooting.Mask, QueryTriggerInteraction.Ignore))
			{
				float impulse = m_Shooting.RayImpact.GetImpulseAtDistance(hitInfo.distance, m_Shooting.MaxDistance);

				// Apply an impact impulse
				if (hitInfo.rigidbody != null)
					hitInfo.rigidbody.AddForceAtPosition(ray.direction * impulse, hitInfo.point, ForceMode.Impulse);

				// Do damage
				float damage = m_Shooting.RayImpact.GetDamageAtDistance(hitInfo.distance, m_Shooting.MaxDistance);
				var damageable = hitInfo.collider.GetComponent<IDamageable>();

				if (damageable != null)
				{
					var damageData = new HealthEventData(-damage, DamageType.Bullet, hitInfo.point, ray.direction, impulse * m_Shooting.RayCount, hitInfo.normal, Player);
					damageable.TakeDamage(damageData);
				}

				if(CheckForEnemyHit(hitInfo))return;
				SurfaceManager.SpawnEffect(hitInfo, SurfaceEffects.BulletHit, 1f);
			}


			WeaponShoot.Send(hitInfo);
		}

		bool CheckForEnemyHit(RaycastHit hitInfo)
		{
			if(hitInfo.transform.root.CompareTag("Enemy"))
			{
				BossAI bossAI = hitInfo.transform.root.GetComponent<BossAI>();
                if(bossAI != null)
                {
                    bossAI.TakeDamage(damage, hitInfo.collider);
                }

				EnemyAI enemyAI = hitInfo.transform.root.GetComponent<EnemyAI>();
                if(enemyAI != null)
                {
				    enemyAI.TakeDamage(damage, hitInfo.collider);
                }
				
				var vnorm = new Quaternion(hitInfo.normal.z, hitInfo.normal.y, -hitInfo.normal.x, 1);
				Instantiate(bloodFX,hitInfo.point,vnorm);
				return true;
			}else
			{
				return false;
			}
		}

		private int GetNextFireModeIndex(int currentIndex) 
		{
			int lastEnumVal = (int)Enum.GetValues(typeof(FireMode)).Cast<FireMode>().Max();

			int i = 1;
			int loopIndex = 0;
			int fireModeIndex = currentIndex;

			if (fireModeIndex == lastEnumVal)
				i = 1;
			else
				i = fireModeIndex * 2;

			while (i <= lastEnumVal)
			{
				if (loopIndex > 20)
					break;

				if (fireMode.Modes.HasFlag((FireMode)i))
				{
					fireModeIndex = i;
					break;
				}

				if (i == lastEnumVal)
					i = 1;
				else
					i *= 2;

				loopIndex++;
			}

			return fireModeIndex;
		}
	}
}