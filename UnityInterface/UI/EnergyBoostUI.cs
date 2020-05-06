using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AirDriVR.UI
{
    public class EnergyBoostUI : MonoBehaviour
    {
        public Image fillImage;
        public Color fillColor = Color.cyan;
        
        private float lastRatio = 0;
        private float colorLerpAmount = 0;
        private Tween neeTweenShake, neeTweenLerp;

        public void SetAmount(float ratio)
        {
            fillImage.DOFillAmount(ratio, 0.2f);
            lastRatio = ratio;
            if (ratio >= 1) fillImage.color = fillColor;
        }

        public void PlayBoopAnimation()
        {
            transform.DOComplete();
            transform.localScale = Vector3.one * 1.2f;
            transform.DOScale(Vector3.one, 0.25f);
        }

        public void PlayNotEnoughEnergyAnimation()
        {
            neeTweenShake.Complete();
            neeTweenShake = transform.DOShakePosition(0.2f, 0.1f, 20);
            
            neeTweenLerp.Complete();
            colorLerpAmount = 1f;
            neeTweenLerp = DOTween.To(() => colorLerpAmount, (x) => colorLerpAmount = x, 0, 0.25f)
                .OnUpdate(() => fillImage.color = Color.Lerp((lastRatio >= 1) ? fillColor : Color.white, Color.red, colorLerpAmount));
        }
    }
}