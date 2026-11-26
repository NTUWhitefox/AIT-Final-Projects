# Design and Evaluation of Haptic-Assisted Motorcycling

**HCI Research Proposal — Group 3**  
Sheng-Yuan Huang · Yu-Chen Sun

## 1. Motivation

Motorcycles and scooters are an important part of everyday transportation in Taiwan, where riders frequently navigate dense urban traffic. Riding requires continuous visual attention to the roadway, surrounding vehicles, intersections, and potential hazards. At the same time, digital services such as navigation systems introduce information that riders may need to consult while moving. Visual interfaces can compete with the attention needed for riding, motivating alternative ways to communicate timely information without requiring riders to look away from the road.

Haptic feedback is one promising option. In particular, a handlebar-mounted interface could provide tactile cues through a surface that riders already hold, without requiring an additional wearable device. However, a cue being perceptible—or a participant being able to identify its pattern in isolation—does not establish that the feedback helps with a riding task. A useful evaluation must examine whether the rider makes the appropriate decision or action in response to information, and whether the feedback performs differently when multiple kinds of events might occur.

This project proposes a low-cost riding simulator and a handlebar-based haptic interface to investigate the **task-level effectiveness of haptic assistance**. Rather than claiming to improve real-world road safety, the study will evaluate performance on controlled, riding-relevant tasks and examine the limitations of using simulation to approximate riding behavior.

## 2. Related Work and Research Opportunity

Prior work has investigated several forms of haptic information delivery for powered two-wheelers. Baldanzini et al. (2011) developed a pressure-based haptic handle and investigated grip contact, stimulus placement, perceptibility, and acceptability. *HaptiMoto* (2014) explored wearable tactile cues for turn-by-turn motorcycle navigation, while Kiss et al. (2018) evaluated wearable tactile route guidance in real riding conditions. These studies demonstrate the feasibility of communicating information through touch, but their interfaces, task settings, and outcome measures vary.

Studies of motorcycle assistance have also moved beyond signal recognition toward behavior. Huth et al. (2012), for example, compared curve-warning interfaces using a motorcycle simulator and considered changes in riding behavior. Work on rider workload, including Will and Schmidt (2015), shows the importance of evaluating interaction while participants are engaged in a primary riding task. More recent simulation research, including Gebretsadik and Suzuki (2024), uses immersive virtual environments and controlled intersection scenarios to study rider reactions and safety-related measures such as response time and time to collision.

Together, these findings motivate a more focused question: **How useful is a convenient, non-wearable haptic interface for completing distinct short riding tasks, and does that usefulness change when different event types occur unpredictably within the same session?** The project does not assume that earlier research lacks behavioral evaluation, nor that haptic feedback will necessarily outperform visual feedback. Its proposed contribution is a controlled comparison of task outcomes across isolated and mixed event contexts, using a relatively accessible simulator setup.

## 3. Proposed Interface and Riding Scenarios

The proposed device delivers vibrations through the motorcycle-style handlebar grips. Left and right actuation zones would support directional cues, while temporal patterns or simultaneous bilateral stimulation could indicate other information. The exact encoding scheme remains provisional: cue patterns must be distinguishable enough to implement the tasks, but **pattern-recognition accuracy is not the primary outcome**. The study instead examines what riders actually do after receiving a cue.

We currently consider three short, event-based scenarios:

1. **Turn navigation:** At an approaching junction, feedback indicates a left or right turn. The relevant outcome is whether the rider follows the correct route, with turn timing and wrong or missed turns recorded.
2. **Obstacle ahead:** An unexpected obstacle or conflict calls for prompt braking or slowing. Relevant outcomes include the initiation of braking, collision occurrence, and minimum time to collision when applicable.
3. **Lane change and blind-spot warning:** A rider is given a reason to change lanes, while a nearby vehicle may make the maneuver unsafe. Feedback may convey a side-specific warning. The study examines whether the rider avoids unsafe changes without simply refusing all lane changes.

These scenarios intentionally require different responses: selecting a direction, reacting to an immediate hazard, and deciding whether to carry out or postpone a maneuver. Events will normally be separated in time so that the initial experiment tests **uncertainty across cue meanings**, not simultaneous overlapping alerts. Additional scenarios or overlapping alerts are possible future extensions, not a requirement of the initial proposal.

## 4. Simulator and System Design

We plan to use a **low-cost VR-based motorcycle riding simulator**, built in Unity with available VR hardware, a fixed seating position, and a physical handlebar-like controller. The controller should support steering, throttle, and braking inputs, while haptic actuators provide feedback on the grips. The simulated environment will include roads, junctions, traffic vehicles, and appropriate visual information such as mirrors or navigation aids. Head tracking may support observation of nearby traffic and blind spots; eye tracking would be optional if supported by the available headset.

VR is preferred over a single-monitor setup primarily because side traffic, head movements, and mirror-checking are relevant to the lane-change scenario, and the group already has VR equipment. A single-monitor version can remain useful for development and as a fallback. The design does not require a motion platform, full-size motorcycle, or high-fidelity vehicle dynamics. Instead, vehicle control will be simplified as necessary to ensure that participants can reliably steer, accelerate, brake, and complete the proposed tasks.

This decision draws on prior motorcycle simulator research, which includes both physical-control and immersive configurations. However, realism cannot be assumed from the presence of a VR headset. Before the main study, pilot testing will check control learnability, event difficulty, participant comfort, and the repeatability of each scenario. This supports a task-appropriate simulation rather than claiming that the system reproduces all physical demands of real riding.

## 5. Proposed Experiment

The experiment will compare performance in **single-context** and **mixed-context** settings, with and without haptic assistance. In the single-context setting, each block contains only one selected kind of event; in the mixed-context setting, the same event types appear in a randomized, non-overlapping sequence. This distinction changes how predictable the meaning of an upcoming cue is without requiring multiple alerts to occur at the same instant.

A provisional design has two factors:

- **Feedback:** without haptic assistance versus with haptic assistance. A consistent visual-information baseline will be defined so that the comparison measures the contribution of tactile cues rather than the complete removal of necessary information.
- **Event context:** single-context versus mixed-context.

The study may additionally compare haptic and visual feedback more directly, consistent with the research questions in the presentation, once the exact baseline is specified. Event frequencies, timing, and scenario difficulty should be matched across comparable conditions. Dummy or non-critical events will help prevent participants from assuming every approaching situation requires braking or avoidance.

Participants will first practice using the simulator and learn any necessary cue meanings. During trials, they will respond by steering, turning, braking, or withholding an unsafe maneuver—not by pressing a dedicated button solely to identify a vibration pattern. Condition order should be counterbalanced where possible, with breaks to reduce fatigue and VR discomfort. The final number of participants and trials will be determined after pilot testing and a feasibility assessment.

## 6. Evaluation Measures

The primary evaluation will use **behavioral task outcomes**, selected according to each scenario rather than forcing all tasks into one generalized score. For navigation, these include correct, missed, and incorrect turns. For obstacle events, measures may include braking response time, collisions, and minimum time to collision. For lane changes, they include unsafe changes, correctly deferred maneuvers, and successful completion when a safe gap becomes available.

Because faster reactions do not automatically mean better decisions, we will also log inappropriate responses where relevant, such as unnecessary braking or rejecting safe lane-change opportunities. Continuous speed and lane-position data may be recorded as secondary measures to check whether the interface disrupts basic riding control. Subjective measures such as NASA-TLX can provide complementary information about perceived workload and distraction, but will not substitute for actual task performance.

The main analysis will ask whether the benefit associated with haptic cues differs between single-context and mixed-context trials. A reduction in benefit under mixed-context conditions would warrant further investigation of possible cue confusion, decision uncertainty, and other contextual effects; it would **not by itself prove that riders misread the patterns**. Recognition or post-trial questions could be used as diagnostics if needed.

## 7. Research Questions and Expected Contribution

**RQ1.** Can a convenient, non-wearable handlebar haptic interface improve performance on individual motorcycle riding tasks relative to an appropriate visual or non-haptic baseline?

**RQ2.** How does the effectiveness of haptic assistance change when the rider must respond to several possible kinds of short events, rather than a single predictable event type?

**RQ3.** To what extent can measurements from a low-cost VR motorcycle simulator inform riding-task evaluation, and what limitations affect their relevance to real-world riding?

The intended contribution is primarily **evaluative and methodological**, rather than the invention of a completely new vibration actuator. The project will explore whether assistance that appears useful in isolated scenarios remains useful when the meaning of the next cue is uncertain, using outcomes that reflect riders' decisions and actions. The resulting experimental framework may also help structure subsequent studies of more complex or overlapping feedback situations.

## 8. Limitations and Scope

The simulator will not fully reproduce real-world motorcycle dynamics, bodily balance, traffic risk, wind, or road and engine vibration. In particular, handlebar vibration generated by actual riding could mask or alter an intentional haptic cue; a stationary rig may therefore overestimate its detectability or usefulness. VR sickness, limited physical feedback, and simplified lane-change dynamics may also affect participant behavior.

Accordingly, the study will avoid claiming direct reductions in real-world crash risk. It will focus on controlled comparisons under the same simulated conditions, while reporting simulator and hardware limitations transparently. Real-road validation, physical background-vibration reproduction, and simultaneous competing alerts are potential follow-up studies.

## References

- Baldanzini et al. (2011). *Design and preliminary testing of an haptic handle for powered two wheelers.* https://doi.org/10.1007/s12544-010-0044-z
- Prasad et al. (2014). *HaptiMoto: Turn-by-turn haptic route guidance interface for motorcyclists.*
- Huth et al. (2012). *Comparison of two warning concepts of an intelligent Curve Warning system for motorcyclists in a simulator study.*
- Will & Schmidt (2015). Motorcycle simulator research on rider workload and secondary-task performance.
- Kiss et al. (2018). *Navigation Systems for Motorcyclists: Exploring Wearable Tactile Feedback for Route Guidance in the Real World.* https://doi.org/10.1145/3173574.3174191
- Yang & Wu (2017). *Primary or secondary tasks? Dual-task interference between cyclist hazard perception and cadence control using cross-modal sensory aids with rider assistance bike computers.*
- Gebretsadik & Suzuki (2024). Motorcycle rider-behavior HMI comparison using VR simulation. https://www.jstage.jst.go.jp/article/jamdsm/18/5/18_2024jamdsm0055/_pdf/-char/en
