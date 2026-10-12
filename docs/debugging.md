# Debugging Java

The design of the plugin's debug engine. The model it plugs into, a Debug Adapter Protocol client, is described in Runesmith's own
[debugging design](https://github.com/RunesmithHub/Runesmith/blob/main/docs/design/debugging.md).

## JDWP

Every JDK has the Java Debug Wire Protocol built in. Runesmith starts the program with
`-agentlib:jdwp=transport=dt_socket,server=y,suspend=y,address=127.0.0.1:0`, reads the port from the first line it prints, connects, and
talks JDWP from C#: a small, documented binary protocol (packets of the VirtualMachine, ReferenceType, Method, ThreadReference,
StackFrame, ObjectReference, StringReference, ArrayReference and EventRequest command sets). The Java plugin's engine maps it to the
DAP model:

- line breakpoints become class prepare and location requests, using the line tables of loaded classes, and are set again as classes load;
- variables come from the frame's slots and objects' fields, with `toString()` invoked for display where the user allows it;
- evaluation covers names, field access, array indexing and method calls without side effects at first, using the Java semantic model to
  resolve names in the paused frame's scope.

Attaching to a program already started with the agent uses the same engine.
