Feature: Recovery
  Pods come and go, and so do the agents and the controller; the dashboard follows without anyone's help.

  Scenario: A pod that is deleted disappears at once, and is shown again when it returns
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the pod "payments-1" of "payments" is deleted
    Then the dashboard shows 0 instances of the service "payments" in the scope "shop" within 20 seconds
    When the pod "payments-1" of "payments" is created
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds

  Scenario: A pod that is not ready is shown as a problem, and as healthy again when it is ready
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the pod "payments-1" of "payments" is no longer ready
    Then the dashboard shows the service "payments" in the scope "shop" as Degraded within 20 seconds
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 20 seconds
    When the pod "payments-1" of "payments" is ready again
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds

  Scenario: When the agent of a scope stops, the scope shows that nobody is listening, and recovers when the agent returns
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the agent is stopped
    Then the dashboard shows that no agent is connected for the scope "shop" within 60 seconds
    When the agent is started
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 90 seconds

  Scenario: A scope whose agent stays away raises a critical alert, which ends when the agent returns
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the agent is stopped
    Then the dashboard shows the critical alert "scope-unknown" for "shop" within 120 seconds
    When the agent is started
    Then the dashboard shows no alert "scope-unknown" within 90 seconds

  Scenario: A restarted controller shows every scope again without help
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the controller is restarted
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 90 seconds

  Scenario: A restarted controller does not alarm anyone while the agents report in again
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the controller is restarted
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 90 seconds
    And the dashboard shows no alerts within 30 seconds

  Scenario: An application that is killed for lack of memory again and again is shown as unhealthy, with the reason
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the application of the pod "payments-1" of "payments" is killed for lack of memory again and again
    Then the dashboard shows the service "payments" in the scope "shop" as Unhealthy within 20 seconds
    And the instance "payments-1" of "payments" in the scope "shop" shows that the container "app" waits with "CrashLoopBackOff" after ending with "OOMKilled" within 20 seconds
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 20 seconds
    When the pod "payments-1" of "payments" is ready again
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds

  Scenario: A Plover sidecar that crash loops degrades the instance but does not make the application look down
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the Plover sidecar of the pod "payments-1" of "payments" crash loops
    Then the dashboard shows the service "payments" in the scope "shop" as Degraded within 20 seconds
    When the pod "payments-1" of "payments" is ready again
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds
