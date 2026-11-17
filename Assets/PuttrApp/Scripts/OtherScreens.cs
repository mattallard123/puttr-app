// The Play and Improve tabs. Play has the games that work and the live trending list; Improve
// says what is not in the spike. (Profile is in ProfileScreen.cs.)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    static class Spacing
    {
        public static T Margin<T>(this T element, float top) where T : VisualElement
        {
            element.style.marginTop = top;
            return element;
        }
    }
}
