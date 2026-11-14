using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

// Other assemblies reach these members by name through reflection; a rename or a type change
// breaks them without a compile error.
public sealed class ReflectionContractTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void ReflectedRuntimeFieldsKeepTheirNamesStaticnessAndTypes()
    {
        AssertField(typeof(LCInteractionAnimationAPI), "coordinator", PrivateStatic,
            typeof(InteractionAnimationCoordinator));
        FieldInfo activeSessions = AssertField(typeof(InteractionAnimationCoordinator), "activeSessions",
            PrivateInstance, typeof(Dictionary<InteractionAnimationHandle, InteractionAnimationSession>));
        Assert.True(typeof(IDictionary).IsAssignableFrom(activeSessions.FieldType));
        AssertField(typeof(InteractionAnimationSession), "presenter", PrivateInstance,
            typeof(IInteractionPresenter));
        AssertField(typeof(LiveBodyAnimatorPresenter), "bodyAnimator", PrivateInstance, typeof(Animator));
        AssertField(typeof(LiveBodyAnimatorPresenter), "fullBodyLayerIndex", PrivateInstance, typeof(int));
        AssertField(typeof(LiveBodyAnimatorPresenter), "firstPersonLayerIndex", PrivateInstance, typeof(int));
        AssertField(typeof(LocalViewmodelPresenter), "viewmodelAnimator", PrivateInstance, typeof(Animator));
    }

    [Fact]
    public void BoundPublicMethodsKeepTheirSignatures()
    {
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryStartInteraction", typeof(bool),
            typeof(InteractionAnimationRequest), typeof(InteractionAnimationHandle).MakeByRefType(),
            typeof(string).MakeByRefType());
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryRegisterInteractionPack", typeof(bool),
            typeof(InteractionAnimationPackDefinition), typeof(string).MakeByRefType());
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryStopInteraction", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(InteractionAnimationStopReason));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "IsInteractionActive", typeof(bool),
            typeof(InteractionAnimationHandle));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryGetActiveInteraction", typeof(bool),
            typeof(GameNetcodeStuff.PlayerControllerB), typeof(InteractionAnimationPresentationKind),
            typeof(InteractionAnimationHandle).MakeByRefType());
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TrySetInteractionBool", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(string), typeof(bool));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TrySetInteractionInt", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(string), typeof(int));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TrySetInteractionFloat", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(string), typeof(float));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryFireInteractionTrigger", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(string));
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryBeginInteractionExit", typeof(bool),
            typeof(InteractionAnimationHandle), typeof(string).MakeByRefType());
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TryPreloadInteractionAssets", typeof(bool),
            typeof(string), typeof(string), typeof(string).MakeByRefType());
        AssertStaticMethod(typeof(LCInteractionAnimationAPI), "TrySetInteractionPostAnimationCallback",
            typeof(bool), typeof(InteractionAnimationHandle), typeof(Action<InteractionAnimationFrame>));
        AssertStaticMethod(typeof(InteractionAnimationManifest), "TryParse", typeof(bool),
            typeof(string), typeof(InteractionAnimationManifest).MakeByRefType(), typeof(string).MakeByRefType());

        PropertyInfo isInitialized = typeof(LCInteractionAnimationAPI).GetProperty(
            "IsInitialized", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(isInitialized);
        Assert.Equal(typeof(bool), isInitialized.PropertyType);
        EventInfo interactionEnded = typeof(LCInteractionAnimationAPI).GetEvent(
            "InteractionEnded", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(interactionEnded);
        Assert.Equal(typeof(EventHandler<InteractionAnimationEndedEventArgs>), interactionEnded.EventHandlerType);
    }

    [Fact]
    public void BoundEnumNamesStayDefined()
    {
        Assert.Equal(
            new[]
            {
                "Requested", "NaturalEnd", "Interrupted", "PlayerInvalidated", "PlayerDied",
                "RoundUnloaded", "PresenterFailure", "Shutdown"
            },
            Enum.GetNames(typeof(InteractionAnimationStopReason)));
        Assert.Equal(
            new[] { "DedicatedLocalViewmodel", "BodyWorld" },
            Enum.GetNames(typeof(InteractionAnimationPresentationKind)));
        Assert.Equal(
            new[] { "RejectIfBusy", "InterruptExisting" },
            Enum.GetNames(typeof(InteractionAnimationConflictPolicy)));
        Assert.Equal(
            new[] { "LocalFirstPerson", "LocalThirdPerson", "Remote" },
            Enum.GetNames(typeof(InteractionAnimationViewer)));
    }

    private static FieldInfo AssertField(Type owner, string name, BindingFlags flags, Type fieldType)
    {
        FieldInfo field = owner.GetField(name, flags);
        Assert.True(field != null, $"{owner.Name}.{name} is missing.");
        Assert.Equal((flags & BindingFlags.Static) != 0, field.IsStatic);
        Assert.Equal(fieldType, field.FieldType);
        return field;
    }

    private static void AssertStaticMethod(Type owner, string name, Type returnType, params Type[] parameters)
    {
        MethodInfo method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
        Assert.True(method != null, $"{owner.Name}.{name} is missing.");
        Assert.Equal(returnType, method.ReturnType);
    }
}
