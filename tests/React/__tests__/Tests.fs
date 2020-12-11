module Fable.Tests.React

open Fable.Jester
open Fable.ReactTestingLibrary

Jest.describe("React tests", (fun () ->

    Jest.test("Counter renders correctly", (fun () ->
        let elem = RTL.render(Counter.Counter())
        Jest.expect(elem.container).toMatchSnapshot()
    ))

    Jest.test("Counter state works", (fun () ->
        let elem = RTL.render(Counter.Counter())
        let header = elem.getByTestId "header"
        let button = elem.getByTestId "button-increment"
        Jest.expect(header).toHaveTextContent("0")
        RTL.fireEvent.click(button)
        Jest.expect(header).toHaveTextContent("1")
    ))

    Jest.test("SpreadSheet renders correctly", (fun () ->
        let elem = RTL.render(SpreadSheet.SpreadSheet())
        Jest.expect(elem.container).toMatchSnapshot()
    ))

    Jest.test("SpreadSheet parser works", (fun () ->
        let spreadsheet = RTL.render(SpreadSheet.SpreadSheet())
        let cells = spreadsheet.getAllByRole("cell")
        let cell = cells.[5]
        RTL.fireEvent.click(cell)
        let input = spreadsheet.getByAltText("cell editor")
        RTL.fireEvent.input(input, [
            event.target [
                Feliz.prop.value "=5"
            ]
        ])
        // Click another cell to remove the editor
        RTL.fireEvent.click(cells.[8])
        Jest.expect(cell).toHaveTextContent("5")
    ))
))