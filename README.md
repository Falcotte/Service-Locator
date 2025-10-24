# Service Locator

A lightweight, extensible Service Locator architecture for Unity that enables clean registration, retrieval, and visualization of runtime services.

This system is designed for projects that require centralized service management, editor-time inspection, and automatic registration/deregistration of services.

## ✨ Features

### Generic Service Locator

* Type-safe registration and retrieval of services.

* Prevents duplicate registration and logs conflicts clearly.

### Base Service Class

* Simplifies setup for MonoBehaviours that act as services.

* Supports auto-registration and clean deregistration.

### Service Inspector Window

* Custom EditorWindow to visualize registered services at runtime.

* Auto-refreshing live view of all active services.

* "Ping" button to locate service instances in the Hierarchy.

## 🧩 Architecture Overview

### Core Components

```IService```

Defines the base interface that all services must implement:

```
public interface IService
{
    void Register();
    void Deregister();
}
```

Every service is expected to self-register and deregister using these methods

```BaseService<T>```

A generic abstract MonoBehaviour that implements IService and provides automatic registration logic:

```
public abstract class BaseService<T> : MonoBehaviour, IService where T : IService
{
    [SerializeField] private bool _autoRegister = true;

    protected virtual void Awake()
    {
        if (_autoRegister)
        {
            Register();
        }
    }

    public void Register() => ServiceLocator.Register<T>(this);
    public void Deregister() => ServiceLocator.Deregister<T>(this);
}
```

This allows you to simply inherit from BaseService<T> and have your service automatically appear in the locator

```ServiceLocator```

A static class that maintains a global dictionary of registered services:

```
public static class ServiceLocator
{
    public static IReadOnlyDictionary<Type, IService> Services => _services;

    public static void Register<T>(IService service);
    public static void Deregister<T>(IService service);
    public static T Get<T>() where T : IService;
}
```

* Raises events on registration and deregistration.

* Logs all registration activity for easier debugging.

* Provides a safe lookup API with warnings for unregistered service requests

```ServiceInspectorEditorWindow```

A Unity Editor tool to **inspect**, **refresh**, and **ping** all registered services in play mode.

Features:

* Toolbar with manual and automatic refresh toggle.

* Responsive table layout showing:

  * Service type

  * Implementation type

  * Actions (e.g., Ping button)

* Visual feedback for empty states (no services/editor not in play mode)

Open it via:

```
Menu → Angry Koala → Services → Service Inspector
```

## 🧠 Usage Example

### 1. Create a Custom Service

```
using AngryKoala.Services;
using UnityEngine;

public interface IAudioService : IService
{
    void PlaySound(AudioClip clip);
}

public class AudioService : BaseService<IAudioService>, IAudioService
{
    public void PlaySound(AudioClip clip)
    {
        Debug.Log($"Playing {clip.name}");
    }
}
```

### 2. Register Automatically

Attach your service script (e.g., AudioService) to a GameObject in your scene.
If _autoRegister is checked, it will be registered automatically on Awake().

### 3. Retrieve Anywhere

```
var audioService = ServiceLocator.Get<IAudioService>();
audioService?.PlaySound(someClip);
```

### 4. Inspect in Editor

While in Play Mode, open:

```
Menu → Angry Koala → Services → Service Inspector
```

You’ll see all currently registered services in a structured table.

## 🧱 Extending the System

You can safely extend the system by:

* Creating custom editor visualizations using ServiceLocator.Services.

* Adding new events or debug tools tied to OnServiceRegistered / OnServiceDeregistered.

* Implementing service interfaces to enforce project-wide contracts.

## 🪲 Debugging Tips

* If a service doesn’t appear in the inspector:

  * Ensure autoRegister is enabled or call Register() manually.

  * Verify the service implements IService correctly.

* Duplicate registration logs a warning but doesn’t overwrite the existing service.

* The inspector only works in play mode.