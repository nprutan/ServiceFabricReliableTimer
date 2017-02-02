
# ServiceFabric ReliableTimer
This is a persistent timer for Service Fabric that allows for triggering a listener on an interval. The interesting part about this timer is that it allows for durable timer countdowns which are not affected by restarting services or rebalanced replicas etc. In other words, the timer works as expected in the sometimes chaotic (but awesome!) environment of Service Fabric.

Usage:

```C#
var intervalTracker = new ReliableTimer(StateManager, "intervalTracker", Context, cancellationToken)
{
    // Change to your desired interval in ms
    ReliableInterval = 60_000,
    Enabled = true
};


intervalTracker.ReliableElapsed += (sender, e) =>
{
    // #InProgress is optional, but it prevents
    // elapsed events from stepping on each other by
    // only executing when set to false (default).
    intervalTracker.InProgress = true;
    
    // Code to execute on schedule here

    intervalTracker.InProgress = false;
};
```

NOTE: Don't forget the using statement :)
