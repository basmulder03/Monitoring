Feature: A call chain through a queue and a database
  The graph shows the real path of a request, also where it crosses a message queue and ends in a database.

  Scenario: A request through the chain shows every hop
    Given the environment is running
    And the dashboard shows the service "testapi" in the scope "shop" as Healthy within 120 seconds
    And the dashboard shows the service "testworker" in the scope "shop" as Healthy within 120 seconds
    And the dashboard shows the service "testdata" in the scope "shop" as Healthy within 120 seconds
    When the application "testapi" is asked for an order 10 times
    Then the dependency graph has a "MQ" call from "testapi" to the queue "orders" within 90 seconds
    And the dependency graph has a "MQ" call from the queue "orders" to "testworker" within 90 seconds
    And the dependency graph has a "DB" call from "testdata" to a database within 90 seconds
