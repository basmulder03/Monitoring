Feature: Recovery
  Pods come and go, and so do the agents and the controller; the dashboard follows without anyone's help.

  Scenario: A pod that is deleted disappears at once, and is shown again when it returns
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the pod "payments-1" of "payments" is deleted
    Then the dashboard shows no live instance of the service "payments" in the scope "shop" within 20 seconds
    When the pod "payments-1" of "payments" is created
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds

  Scenario: When the agent of a scope stops, the scope shows that nobody is listening, and recovers when the agent returns
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the agent is stopped
    Then the dashboard shows that no agent is connected for the scope "shop" within 60 seconds
    When the agent is started
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 90 seconds

  Scenario: A restarted controller shows every scope again without help
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the controller is restarted
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 90 seconds
