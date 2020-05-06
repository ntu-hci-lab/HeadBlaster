﻿using System;
using System.Collections;
using System.Collections.Generic;
using AirDriVR.UI;
using DG.Tweening;
using HTC.UnityPlugin.Vive;
using PathCreation.Examples;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Valve.VR;
using Valve.VR.InteractionSystem.Sample;

namespace AirDriVR
{
    public class SurfController : MonoBehaviour
    {
        #region Fields

        public enum GameState
        {
            WaitingToStart,
            GettingReady,
            Running,
            Finished
        }

        public GameState gameState = GameState.WaitingToStart;

        public HoverboardGameStateUI gameStateUI;

        public bool enableMouseControl = true;

        public Text speedText;

        public float playerHeight = 1.8f;

        public new Transform camera;
        public Transform cameraOffset;
        public List<Transform> bridgeParents;
        public float playerBridgeHeightRatio = 0.8f;

        public AudioSource bgm;
        public AudioLowPassFilter bgmFilter;
        public AudioSource boardSFX;

        public Rigidbody rgd;

        public float speedCap = 20f;
        public AnimationCurve accelerationCurve;

        public bool sidewaysUseAcceleration = false;
        public float sidewaysAcceleration = 5f;
        public float sidewaysMaxVelocity = 5f;
        public float sidewaysVelocityMaxAngle = 30f;
        public float sidewaysCollisionDisableDuration = 0.5f;
        public AudioSource sidewaysCollisionSound;

        private bool isSidewaysDisabled = false;
        private float sidewaysMaxDotProduct;
        
        public bool isBoostMode = false;
        public float boostDuration = 4f;
        public float boostSpeedCap = 40f;
        public AnimationCurve boostAccelerationCurve;
        public PostProcessVolume boostFXVolume;
        public ParticleSystem boostNovaFX;
        public AudioSource boostSFX;
        public EnergyBoostUI boostUI;
        public float boostOverrideForceAmount = 0.75f;
        
        private float boostTimer = 0;
        
        public AnimationCurve fogAccelerationCurve;
        public PostProcessVolume fogFXVolume;
        
        private bool isInFog = false;

        public float stunDuration = 3f;
        public PostProcessVolume stunFXVolume;
        public AudioSource stunSFX;
        
        private bool isStunned = false;
        private float stunTimer = 0;

        public Text lapTimeText;
        private float lapTime = 0;
        
        private int boostCount = 0;
        private int bridgeCount = 0;
        private int sideCount = 0;
        private float fogEnterTime = 0;
        private float inFogTime = 0;
        public bool isLogging = false;
        private bool isChanged = true;
        public string userID = "";
        public bool isWithHeadJets = true;

        #endregion

        private void Awake()
        {
            sidewaysMaxDotProduct = Mathf.Sin(sidewaysVelocityMaxAngle * Mathf.Deg2Rad);
        }

        private void Update()
        {
            switch (gameState)
            {
                case GameState.WaitingToStart:
                    if (Input.GetMouseButtonDown(0)
                        || ViveInput.GetPressDown(HandRole.LeftHand, ControllerButton.FullTrigger)
                        || ViveInput.GetPressDown(HandRole.RightHand, ControllerButton.FullTrigger))
                    {
                        playerHeight = (camera.localPosition.y == 0) ? playerHeight : camera.localPosition.y;
                        cameraOffset.localPosition = new Vector3(-camera.localPosition.x, 0, -camera.localPosition.z);
                        bridgeParents.ForEach(b => b.localPosition = playerBridgeHeightRatio * playerHeight * Vector3.up);
                    }

                    if (Input.GetMouseButtonDown(1)
                        || ViveInput.GetPressDown(HandRole.LeftHand, ControllerButton.Grip)
                        || ViveInput.GetPressDown(HandRole.RightHand, ControllerButton.Grip))
                    {
                        // TODO: switch back when done testing
                        gameStateUI.StartCountdownSequence(OnCountdownFinished);
                        gameState = GameState.GettingReady;
                    }

                    break;
                case GameState.Running:
                    UpdateRunning();
                    break;
                case GameState.Finished:
                    
                    UpdateFX();

                    if (Input.GetMouseButtonDown(1)
                        || (ViveInput.GetPressDown(HandRole.LeftHand, ControllerButton.Grip)
                            && ViveInput.GetPressDown(HandRole.RightHand, ControllerButton.Grip)))
                    {
                        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                    }

                    break;
            }
        }

        private void UpdateRunning()
        {
            if (enableMouseControl)
            {
                camera.localPosition = Vector3.up * (Input.GetMouseButton(2) ? playerHeight * 0.5f : playerHeight);
            }

            UpdateFX();
            if (isLogging && isChanged)
            {
                Debug.LogFormat("Boost:{0}, Bridge:{1}, Side:{2}, FogTime:{3}", boostCount, bridgeCount, sideCount,
                    inFogTime);
                isChanged = false;
            }
        }

        void UpdateFX()
        {
            boardSFX.pitch = Mathf.Lerp(boardSFX.pitch, rgd.velocity.z / speedCap + 0.5f,
                5f * Time.fixedDeltaTime);
            boostFXVolume.weight = Mathf.Lerp(boostFXVolume.weight, (isBoostMode) ? 1 : 0,
                5f * Time.fixedDeltaTime);
            boostSFX.volume = Mathf.Lerp(boostSFX.volume, (isBoostMode) ? 1 : 0,
                10f * Time.fixedDeltaTime);
            boostSFX.pitch = Mathf.Lerp(boostSFX.pitch, (isBoostMode) ? rgd.velocity.z / boostSpeedCap : 0.1f,
                5f * Time.fixedDeltaTime);
            fogFXVolume.weight = Mathf.Lerp(fogFXVolume.weight, (isInFog && !isBoostMode) ? 1 : 0,
                5f * Time.fixedDeltaTime);
            stunFXVolume.weight = Mathf.Lerp(stunFXVolume.weight, (isStunned) ? 1 : 0,
                5f * Time.fixedDeltaTime);
            bgmFilter.cutoffFrequency = Mathf.Lerp(bgmFilter.cutoffFrequency, (isStunned) ? 300 : 22000,
                5f * Time.fixedDeltaTime);
            bgm.pitch = Mathf.Lerp(bgm.pitch, (isInFog && !isBoostMode) ? 0.5f : 1,
                5f * Time.fixedDeltaTime);
        }

        private void FixedUpdate()
        {
            switch (gameState)
            {
                case GameState.Running:
                    var currentSpeed = rgd.velocity.z;
                    if (isStunned)
                    {
                        stunTimer -= Time.fixedDeltaTime;
                        if (stunTimer <= 0) isStunned = false;
                    }
                    else if (isBoostMode)
                    {
                        rgd.velocity += boostAccelerationCurve.Evaluate(currentSpeed / boostSpeedCap) * Time.fixedDeltaTime * Vector3.forward;
                        boostTimer -= Time.fixedDeltaTime;
                        boostUI.SetAmount(boostTimer / boostDuration);
                        if (boostTimer <= 0)
                        {
                            isBoostMode = false;
                            boostUI.gameObject.SetActive(false);
                        }
                    }
                    else if (isInFog)
                    {
                        rgd.velocity += fogAccelerationCurve.Evaluate(currentSpeed / speedCap) * Time.fixedDeltaTime * Vector3.forward;
                    }
                    else
                    {
                        rgd.velocity += accelerationCurve.Evaluate(currentSpeed / speedCap) * Time.fixedDeltaTime * Vector3.forward;
                    }
                    
                    speedText.text = $"{rgd.velocity.z * 3.6f:F1} kmh";
                    
                    lapTime += Time.fixedDeltaTime;
                    lapTimeText.text = TimeSpan.FromSeconds(lapTime).ToString(@"m\:ss\.fff");


                    if (!isStunned)
                    {
                        if (sidewaysUseAcceleration)
                        {
                            rgd.velocity += GetLateralInput() * sidewaysAcceleration * Time.fixedDeltaTime * Vector3.right;
                        }
                        else if(!isSidewaysDisabled)
                        {
                            var v = rgd.velocity;
                            v.x = Mathf.MoveTowards(v.x, GetSidewaysVelocity() * sidewaysMaxVelocity, sidewaysAcceleration * Time.fixedDeltaTime);
                            rgd.velocity = v;
                        }
                    }

                    if (transform.position.z >= 2000f)
                    {
                        gameState = GameState.Finished;
                        isBoostMode = false;
                        boostUI.gameObject.SetActive(false);
                        speedText.transform.parent.gameObject.SetActive(false);
                        lapTimeText.transform.parent.gameObject.SetActive(false);
                        gameStateUI.Finish(lapTime);
                        
                        // Logger for the surfing sutdy
                        
                    }
                    break;
                case GameState.Finished:
                    rgd.velocity -= new Vector3(Mathf.Min(rgd.velocity.x, 10f * Time.fixedDeltaTime), 0, Mathf.Min(rgd.velocity.z, 10f * Time.fixedDeltaTime));
                    break;
            }
        }

        private float GetSidewaysVelocity()
        {
            return Mathf.Clamp(Vector3.Dot(camera.up, Vector3.right) / sidewaysMaxDotProduct, -1, 1)
                   + Input.GetAxis("Horizontal");
        }

        private float GetLateralInput()
        {
            return isSidewaysDisabled
                ? 0
                : (Input.GetAxis("Horizontal") + ViveInput.GetTriggerValue(HandRole.RightHand) - ViveInput.GetTriggerValue(HandRole.LeftHand));
        }

        private void OnCountdownFinished()
        {
            gameState = GameState.Running;
        }

        private void OnTriggerEnter(Collider other)
        {
            switch (other.tag)
            {
                case "Boost":
                    isBoostMode = true;
                    boostSFX.volume = 0.75f;
                    boostTimer = boostDuration;
                    boostUI.gameObject.SetActive(true);
                    boostUI.SetAmount(1);
                    boostUI.PlayBoopAnimation();
                    boostNovaFX.Play(true);
                    other.gameObject.SetActive(false);
                    StartCoroutine(BoostOverrideCoroutine());
                    boostCount += 1; // Log for the surfing study
                    isChanged = true; // Log for the surfing study
                    break;
                case "Bridge":
                    if (!isBoostMode)
                    {
                        rgd.velocity = Vector3.zero;
                        isStunned = true;
                        stunTimer = stunDuration;
                        stunFXVolume.weight = 1;
                        stunSFX.Play();
                        bgmFilter.cutoffFrequency = 300;
                        bridgeCount += 1; // Log for the surfing study
                        isChanged = true; // Log for the surfing study
                    }
                    other.gameObject.SetActive(false);
                    break;
                case "SlowingFog":
                    if (!isInFog) // Log for the surfing study
                    {
                        fogEnterTime = Time.time;
                        Debug.LogFormat("Enter fog fogEnterTime is {0}", fogEnterTime);
                    }
                    isInFog = true;
                    break;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            switch (other.tag)
            {
                case "SlowingFog":
                    if (isInFog) // Log for the surfing study
                    {
                        inFogTime += Time.time - fogEnterTime;
                        Debug.LogFormat("Exit fog, inFogTime is {0}, Current time is {1}",inFogTime,Time.time);
                        isChanged = true; // Log for the surfing study
                    }
                    isInFog = false;
                    break;
            }
        }

        private void OnCollisionEnter(Collision other)
        {
            if (!isSidewaysDisabled && other.gameObject.CompareTag("Boundary"))
            {
                isSidewaysDisabled = true;
                StartCoroutine(SidewaysReenableCoroutine());
                sideCount += 1; // Log for the surfing study
                isChanged = true; // Log for the surfing study
            }
        }

        private IEnumerator SidewaysReenableCoroutine()
        {
            sidewaysCollisionSound.Play();
            var endTime = Time.time + sidewaysCollisionDisableDuration;
            while (Time.time < endTime)
            {
                yield return null;
            }
            isSidewaysDisabled = false;
        }
        
        private IEnumerator BoostOverrideCoroutine()
        {
            var endTime = Time.time + boostDuration;
            while (Time.time < endTime)
            {
                AirDriVRSystem.SetForce(new Vector2(0, -boostOverrideForceAmount), 10, AirDriVRSystem.OverrideMode.YOnly);
                yield return null;
            }
        }
    }
}
